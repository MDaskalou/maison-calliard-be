#!/usr/bin/env python3
"""Check or reset one existing DEV admin password after the DEV guard has passed.

The command is read-only unless --execute is present. The expected DEV project
reference and the admin email are required arguments. The reference is never
read from DevConnection. Only the verified DevConnection can be used, and only
after the selected account has been confirmed. Saved User Secrets are only read.
Connection strings, passwords, and password hashes are not printed.
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

import dev_api_guard as guard

ALLOWED_EMAIL = "info@fuegodanceschool.se"
SECRETS_UNCHANGED = "Ingen sparad User Secret ändrades."
RESET_CONNECTION_ENV = "MAISON_DEV_ADMIN_RESET_CONNECTION"
CONNECTION_ENV_PREFIXES = ("ConnectionStrings__", "CUSTOMCONNSTR_")

Executor = Callable[[str, str, str, str, str], int]


@dataclass(frozen=True)
class ResetEntry:
    account_confirmation: str
    password: str
    repeated_password: str


ConfirmationReader = Callable[[], ResetEntry | None]


@dataclass(frozen=True, repr=False)
class ResetPlan:
    ok: bool
    messages: tuple[str, ...]
    connection: str | None = None
    prod_ref: str | None = None

    def __repr__(self) -> str:
        return f"ResetPlan(ok={self.ok}, messages={self.messages!r})"


def main(
    argv: list[str] | None = None,
    loader=None,
    confirmation_reader: ConfirmationReader | None = None,
    executor: Executor | None = None,
) -> int:
    args = parse_args(argv)
    if args.execute and args.dry_run:
        print("Återställning och skrivskyddat läge kan inte kombineras. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1
    if args.email != ALLOWED_EMAIL:
        print("Endast info@fuegodanceschool.se kan återställas.")
        print(SECRETS_UNCHANGED)
        return 1

    try:
        user_secrets, merged = (loader or guard.load_all)()
    except guard.GuardFailure as failure:
        print(failure)
        print(SECRETS_UNCHANGED)
        return 1

    plan = plan_reset(user_secrets, merged, args.expected_dev_ref, args.email)
    for message in plan.messages:
        print(message)
    if not plan.ok or plan.connection is None or plan.prod_ref is None:
        print(SECRETS_UNCHANGED)
        return 1

    if not args.execute:
        print("Skrivskyddat läge: inget lösenord ändrades.")
        print(SECRETS_UNCHANGED)
        return 0

    try:
        entered = (confirmation_reader or read_confirmation)()
    except (EOFError, KeyboardInterrupt):
        print()
        print("Återställningen avbröts. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1
    if entered is None:
        print("Återställningen avbröts. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1
    if entered.account_confirmation != args.email:
        print("Det valda kontot bekräftades inte. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1
    if entered.password != entered.repeated_password:
        print("Lösenorden matchar inte. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1
    if not password_is_strong(entered.password):
        print("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1

    try:
        code = (executor or launch_resetter)(
            plan.connection,
            args.expected_dev_ref,
            plan.prod_ref,
            args.email,
            entered.password,
        )
    except AssertionError:
        raise
    except Exception:
        print("Återställningen avbröts. Inget ändrades.")
        print(SECRETS_UNCHANGED)
        return 1

    print(SECRETS_UNCHANGED)
    return 0 if code == 0 else 1


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Skrivskyddad kontroll av DEV-lösenordsåterställning. --execute krävs för att ändra lösenordet.",
    )
    parser.add_argument("--expected-dev-ref", required=True)
    parser.add_argument("--email", required=True)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args(argv)


def plan_reset(
    user_secrets: dict[str, str],
    merged_settings: dict[str, str],
    expected_dev_ref: str,
    email: str,
) -> ResetPlan:
    if email != ALLOWED_EMAIL:
        return ResetPlan(False, ("Endast info@fuegodanceschool.se kan återställas.",))

    result = guard.evaluate(user_secrets, merged_settings, expected_dev_ref)
    if not result.ok or result.overrides is None:
        return ResetPlan(False, result.messages)

    dev_connection = verified_dev_connection(user_secrets, result.overrides)
    prod_connection = user_secrets.get("ConnectionStrings:DefaultConnection", "")
    if dev_connection is None:
        return ResetPlan(False, ("Processen kunde inte låsa anslutningen till DEV.",))
    if dev_connection == prod_connection:
        return ResetPlan(False, ("DEV och PROD är samma Supabase-projekt.",))

    try:
        dev = guard.parse_connection(dev_connection, "DevConnection")
        prod = guard.parse_connection(prod_connection, "DefaultConnection")
    except guard.GuardFailure:
        return ResetPlan(False, ("Anslutningen kunde inte verifieras. Inget ändrades.",))

    expected = expected_dev_ref.strip().lower()
    if dev.project_ref != expected:
        return ResetPlan(False, ("DevConnection matchar inte den angivna DEV-referensen.",))
    if dev.project_ref == prod.project_ref:
        return ResetPlan(False, ("DEV och PROD är samma Supabase-projekt.",))

    return ResetPlan(
        True,
        (
            "Säkerhetskontrollerna passerade.",
            "DEV-projektet matchar den angivna referensen och är ett annat projekt än PROD.",
            "Endast info@fuegodanceschool.se kan återställas.",
        ),
        dev_connection,
        prod.project_ref,
    )


def verified_dev_connection(user_secrets: dict[str, str], overrides: dict[str, str]) -> str | None:
    dev = user_secrets.get("ConnectionStrings:DevConnection", "")
    if not dev or overrides.get("ConnectionStrings__DefaultConnection") != dev:
        return None
    return dev


def password_is_strong(password: str) -> bool:
    if password == "":
        return False
    length = utf16_length(password)
    trimmed = utf16_length(password.strip())
    return length >= 12 and length <= 256 and trimmed >= 12


def utf16_length(value: str) -> int:
    return len(value.encode("utf-16-le")) // 2


def read_confirmation() -> ResetEntry | None:
    try:
        account = input(f"Skriv {ALLOWED_EMAIL} för att bekräfta kontot: ")
        first = getpass_password("Nytt lösenord: ")
        second = getpass_password("Upprepa lösenordet: ")
    except (EOFError, KeyboardInterrupt):
        return None
    return ResetEntry(account, first, second)


def password_payload(password: str) -> bytes:
    encoded = password.encode("utf-8")
    if len(encoded) > 1024:
        raise ValueError("password encoding is too long")
    return str(len(encoded)).encode("ascii") + b"\n" + encoded


def getpass_password(prompt: str) -> str:
    import getpass

    return getpass.getpass(prompt)


def launch_resetter(
    connection: str,
    expected_ref: str,
    prod_ref: str,
    email: str,
    password: str,
    spawn: Callable[..., subprocess.CompletedProcess[object]] | None = None,
    dll_path: Path | None = None,
) -> int:
    if email != ALLOWED_EMAIL:
        print("Endast info@fuegodanceschool.se kan återställas.")
        return 1
    if not connection or not expected_ref.strip() or not prod_ref.strip():
        print("Återställningen avbröts. Inget ändrades.")
        return 1

    run = spawn or subprocess.run
    root = guard.repo_root()
    project = root / "tools" / "DevAdminPasswordReset" / "MaisonCalliard.DevAdminPasswordReset.csproj"
    try:
        build = run(
            ["dotnet", "build", str(project), "-c", "Release", "--verbosity", "quiet", "--nologo"],
            cwd=root,
            env=scrubbed_environment(),
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
    except OSError:
        print("Återställningsverktyget kunde inte byggas. Inget ändrades.")
        return 1
    if build.returncode != 0:
        print("Återställningsverktyget kunde inte byggas. Inget ändrades.")
        return 1

    if dll_path is not None:
        dll = dll_path
    else:
        dll = locate_reset_dll(root)
        if dll is None:
            print("Återställningsverktyget kunde inte byggas. Inget ändrades.")
            return 1

    try:
        payload = password_payload(password)
    except ValueError:
        print("Återställningen avbröts. Inget ändrades.")
        return 1

    try:
        completed = run(
            [
                "dotnet",
                str(dll),
                "--expected-dev-ref",
                expected_ref,
                "--prod-ref",
                prod_ref,
                "--email",
                email,
            ],
            cwd=root,
            env=child_environment(connection),
            input=payload,
            check=False,
        )
    except OSError:
        print("Återställningen avbröts. Inget ändrades.")
        return 1
    return completed.returncode


def locate_reset_dll(root: Path) -> Path | None:
    directory = root / "tools" / "DevAdminPasswordReset" / "bin" / "Release"
    matches = sorted(directory.glob("*/MaisonCalliard.DevAdminPasswordReset.dll"))
    if not matches or not matches[-1].is_file():
        return None
    return matches[-1]


def child_environment(connection: str) -> dict[str, str]:
    environment = scrubbed_environment()
    environment[RESET_CONNECTION_ENV] = connection
    return environment


def scrubbed_environment() -> dict[str, str]:
    environment = os.environ.copy()
    for key in list(environment):
        if key == RESET_CONNECTION_ENV or key.startswith(CONNECTION_ENV_PREFIXES):
            del environment[key]
    return environment


if __name__ == "__main__":
    sys.exit(main())
