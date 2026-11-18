#!/usr/bin/env python3
"""Start the local API only after DEV and PROD Supabase projects have been checked.

The expected DEV project reference is a required argument. It is never read from
DevConnection. Connection strings and passwords are not printed.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import unquote, urlparse

USER_SECRETS_ID = "b40ac044-ac41-4b8f-9d24-65159b01f14c"
LOCAL_ORIGIN = "http://localhost:3031"
PROJECT_REF = re.compile(r"^[a-z0-9]{20}$")
USERNAME_REF = re.compile(r"^postgres\.([a-z0-9]{20})$")
DB_HOST_REF = re.compile(r"^db\.([a-z0-9]{20})\.supabase\.co$", re.IGNORECASE)
API_HOST_REF = re.compile(r"^([a-z0-9]{20})\.supabase\.co$", re.IGNORECASE)
ACCEPTED_SSL = {"require", "verifyca", "verify-ca", "verifyfull", "verify-full"}
PLACEHOLDER_PASSWORDS = {
    "your_local_password_here",
    "password",
    "postgres",
    "changeme",
    "change_me",
    "placeholder",
    "secret",
    "your-password",
    "your_password",
    "replace_me",
    "todo",
    "***",
    "xxx",
}
STRIPE_SECRET_SUFFIX = ":SecretKey"


class GuardFailure(Exception):
    """A check failed. The message must not contain a secret."""


@dataclass(frozen=True)
class ParsedConnection:
    project_ref: str
    ssl_mode: str | None
    trusts_any_certificate: bool
    password_missing: bool
    password_is_placeholder: bool


@dataclass(frozen=True)
class GuardResult:
    ok: bool
    messages: tuple[str, ...]
    overrides: dict[str, str] | None


def main(argv: list[str] | None = None, starter=None, loader=None) -> int:
    args = parse_args(argv)
    try:
        user_secrets, merged = (loader or load_all)()
    except GuardFailure as failure:
        print(failure)
        return 1

    result = evaluate(user_secrets, merged, args.expected_dev_ref)
    for message in result.messages:
        print(message)
    if not result.ok or result.overrides is None:
        return 1
    if args.dry_run:
        print("Dry-run: ingen server startades.")
        return 0

    launch = starter or start_api
    return launch(child_environment(result.overrides))


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Starta API:t mot verifierad DEV.")
    parser.add_argument("--expected-dev-ref", required=True)
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args(argv)


def evaluate(
    user_secrets: dict[str, str],
    merged_settings: dict[str, str],
    expected_dev_ref: str,
) -> GuardResult:
    failures: list[str] = []
    expected = expected_dev_ref.strip().lower()
    if not PROJECT_REF.fullmatch(expected):
        failures.append("Den förväntade DEV-referensen saknas eller har fel format.")

    dev = read_connection(user_secrets, "ConnectionStrings:DevConnection", "DevConnection", failures)
    prod = read_connection(user_secrets, "ConnectionStrings:DefaultConnection", "DefaultConnection", failures)

    if dev is not None:
        if dev.password_missing:
            failures.append("DevConnection saknar lösenord.")
        elif dev.password_is_placeholder:
            failures.append("DevConnection använder ett platshållarlösenord.")
        if dev.ssl_mode not in ACCEPTED_SSL:
            failures.append("DevConnection kräver inte TLS.")
        if dev.trusts_any_certificate:
            failures.append("DevConnection stänger av certifikatkontrollen.")
        if PROJECT_REF.fullmatch(expected) and dev.project_ref != expected:
            failures.append("DevConnection matchar inte den angivna DEV-referensen.")

    if dev is not None and prod is not None and dev.project_ref == prod.project_ref:
        failures.append("DEV och PROD är samma Supabase-projekt.")

    if failures or dev is None:
        return GuardResult(False, tuple(failures), None)

    try:
        overrides = process_overrides(
            user_secrets["ConnectionStrings:DevConnection"],
            user_secrets["ConnectionStrings:DefaultConnection"],
            merged_settings,
            dev.project_ref,
            prod.project_ref if prod is not None else "",
        )
    except GuardFailure as failure:
        return GuardResult(False, (str(failure),), None)

    messages = [
        "Säkerhetskontrollerna passerade.",
        "DEV-projektet matchar den angivna referensen och är ett annat projekt än PROD.",
        "Processen sätter ASPNETCORE_ENVIRONMENT=Development.",
        "Processen sätter ConnectionStrings__DefaultConnection till den verifierade DevConnection.",
        "Processen sätter Cors__AllowedOriginsCsv och Auth__AllowedBrowserOriginsCsv till http://localhost:3031.",
        *integration_messages(merged_settings, overrides),
    ]
    return GuardResult(True, tuple(messages), overrides)


def read_connection(
    secrets: dict[str, str],
    key: str,
    label: str,
    failures: list[str],
) -> ParsedConnection | None:
    raw = secrets.get(key, "")
    if not raw.strip():
        failures.append(f"{label} saknas i User Secrets.")
        return None
    try:
        return parse_connection(raw, label)
    except GuardFailure as failure:
        failures.append(str(failure))
        return None


def parse_connection(raw: str, label: str) -> ParsedConnection:
    if "://" in raw:
        return parse_uri(raw, label)
    return parse_keywords(raw, label)


def parse_keywords(raw: str, label: str) -> ParsedConnection:
    try:
        properties = keyword_properties(raw)
    except GuardFailure:
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.") from None
    if not properties:
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.")

    host = first(properties, "host", "hostaddress", "server", "datasource")
    username = first(properties, "username", "userid", "uid")
    password = first(properties, "password", "pwd")
    database = first(properties, "database", "dbname")
    if not host or not username or not database:
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.")
    if looks_like_sql_server(properties, host):
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.")

    return ParsedConnection(
        project_ref=project_ref(username, host, label),
        ssl_mode=normalize_ssl(first(properties, "sslmode")),
        trusts_any_certificate=is_true(first(properties, "trustservercertificate")),
        password_missing=password.strip() == "",
        password_is_placeholder=is_placeholder_password(password),
    )


def parse_uri(raw: str, label: str) -> ParsedConnection:
    parsed = urlparse(raw)
    if parsed.scheme not in {"postgres", "postgresql"} or not parsed.hostname:
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.")
    query = dict(pair.split("=", 1) if "=" in pair else (pair, "") for pair in parsed.query.split("&") if pair)
    username = unquote(parsed.username or "")
    password = unquote(parsed.password or "")
    database = parsed.path.lstrip("/")
    if not username or not database:
        raise GuardFailure(f"{label} är inte en giltig PostgreSQL-anslutningssträng.")
    return ParsedConnection(
        project_ref=project_ref(username, parsed.hostname, label),
        ssl_mode=normalize_ssl(query.get("sslmode")),
        trusts_any_certificate=is_true(query.get("trustservercertificate")),
        password_missing=password.strip() == "",
        password_is_placeholder=is_placeholder_password(password),
    )


def keyword_properties(raw: str) -> dict[str, str]:
    properties: dict[str, str] = {}
    for part in split_properties(raw):
        if not part.strip():
            continue
        if "=" not in part:
            raise GuardFailure("invalid")
        key, value = part.split("=", 1)
        normalized = re.sub(r"[\s_]", "", key).lower()
        if not normalized:
            raise GuardFailure("invalid")
        properties[normalized] = unquote_value(value.strip())
    return properties


def split_properties(raw: str) -> list[str]:
    parts: list[str] = []
    buffer: list[str] = []
    quote: str | None = None
    index = 0
    while index < len(raw):
        character = raw[index]
        if quote is not None:
            if character == quote and index + 1 < len(raw) and raw[index + 1] == quote:
                buffer.append(character)
                buffer.append(raw[index + 1])
                index += 2
                continue
            buffer.append(character)
            if character == quote:
                quote = None
            index += 1
            continue
        if character in {"'", '"'}:
            quote = character
            buffer.append(character)
            index += 1
            continue
        if character == ";":
            parts.append("".join(buffer))
            buffer = []
            index += 1
            continue
        buffer.append(character)
        index += 1
    if quote is not None:
        raise GuardFailure("invalid")
    parts.append("".join(buffer))
    return parts


def unquote_value(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
        return value[1:-1].replace(value[0] * 2, value[0])
    return value


def project_ref(username: str, host: str, label: str) -> str:
    refs: set[str] = set()
    username_match = USERNAME_REF.fullmatch(username.strip())
    if username_match:
        refs.add(username_match.group(1))
    host_name = host.strip().lower().rstrip(".")
    for pattern in (DB_HOST_REF, API_HOST_REF):
        host_match = pattern.fullmatch(host_name)
        if host_match and not host_name.endswith(".pooler.supabase.com"):
            refs.add(host_match.group(1))
    if len(refs) != 1:
        raise GuardFailure(f"{label} saknar Supabase-projektreferens.")
    return next(iter(refs))


def project_ref_from_url(url: str) -> str | None:
    parsed = urlparse(url.strip())
    if not parsed.hostname:
        return None
    match = API_HOST_REF.fullmatch(parsed.hostname.lower())
    if not match:
        return None
    return match.group(1)


def process_overrides(
    dev_connection: str,
    prod_connection: str,
    settings: dict[str, str],
    dev_ref: str,
    prod_ref: str,
) -> dict[str, str]:
    if dev_connection == prod_connection:
        raise GuardFailure("DEV och PROD är samma Supabase-projekt.")
    overrides = {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ConnectionStrings__DefaultConnection": dev_connection,
        "Cors__AllowedOriginsCsv": LOCAL_ORIGIN,
        "Auth__AllowedBrowserOriginsCsv": LOCAL_ORIGIN,
        "ASPNETCORE_URLS": "http://localhost:5089",
        "Resend__Enabled": "false",
    }
    isolate_supabase(settings, overrides, dev_ref, prod_ref)
    isolate_stripe(settings, overrides)
    if overrides["ConnectionStrings__DefaultConnection"] != dev_connection:
        raise GuardFailure("Processen kunde inte låsa anslutningen till DEV.")
    if overrides["ConnectionStrings__DefaultConnection"] == prod_connection:
        raise GuardFailure("Processen kunde inte blockera PROD.")
    return overrides


def isolate_supabase(settings: dict[str, str], overrides: dict[str, str], dev_ref: str, prod_ref: str) -> None:
    url = settings.get("Supabase:Url", "").strip()
    service_role = settings.get("Supabase:ServiceRoleKey", "").strip()
    bucket = settings.get("Supabase:StorageBucket", "").strip()
    if not url and not service_role:
        return
    if not url or not service_role or not bucket:
        return
    ref = project_ref_from_url(url)
    if ref is None:
        raise GuardFailure("Supabase:Url kan inte verifieras som ett Supabase-projekt.")
    if ref == prod_ref:
        overrides["Supabase__ServiceRoleKey"] = ""
        return
    if ref != dev_ref:
        raise GuardFailure("Supabase:Url pekar på ett projekt som varken är angiven DEV eller identifierad PROD.")


def isolate_stripe(settings: dict[str, str], overrides: dict[str, str]) -> None:
    for key, value in settings.items():
        if not key.startswith("Stripe:") or not key.endswith(STRIPE_SECRET_SUFFIX):
            continue
        kind = classify_stripe_secret(value)
        if kind == "live":
            overrides[key.replace(":", "__")] = ""
        elif kind == "unknown":
            raise GuardFailure("En Stripe-hemlighet kan inte verifieras som testnyckel. Start avbryts.")


def integration_messages(settings: dict[str, str], overrides: dict[str, str]) -> list[str]:
    messages = ["Resend stängs av i processen via Resend__Enabled=false."]
    if "Supabase__ServiceRoleKey" in overrides:
        messages.append("Supabase Storage pekar på PROD. Processen tömmer Supabase__ServiceRoleKey och använder lokal fillagring.")
    elif supabase_targets_dev(settings):
        messages.append("Supabase Storage pekar på samma projekt som DEV och lämnas aktiv.")
    else:
        messages.append("Supabase Storage är inte konfigurerat och använder lokal fillagring.")

    live = [key for key, value in overrides.items() if key.startswith("Stripe__") and value == ""]
    test_keys = [
        key for key, value in settings.items()
        if key.startswith("Stripe:") and key.endswith(STRIPE_SECRET_SUFFIX) and classify_stripe_secret(value) == "test"
    ]
    if live:
        messages.append("Stripe-live-nycklar töms i processen så att inga produktionsbetalningar kan skapas.")
    elif test_keys:
        messages.append("Stripe använder testnycklar och lämnas oförändrat.")
    else:
        messages.append("Ingen Stripe-hemlighet är konfigurerad.")

    seed_count = sum(1 for key, value in settings.items() if key.startswith("Auth:SeedAdmins:") and key.endswith(":Email") and value.strip())
    if seed_count:
        messages.append(
            f"AdminUserSeeder har {seed_count} konfigurerade konton och skriver bara via DefaultConnection, som processen sätter till DEV."
        )
    else:
        messages.append("AdminUserSeeder har inga konton att skapa.")
    messages.append("Ingen databasmigration eller EnsureCreated körs vid start.")
    return messages


def supabase_targets_dev(settings: dict[str, str]) -> bool:
    return bool(
        settings.get("Supabase:Url", "").strip()
        and settings.get("Supabase:ServiceRoleKey", "").strip()
        and settings.get("Supabase:StorageBucket", "").strip()
    )


def classify_stripe_secret(value: str) -> str:
    secret = value.strip()
    if not secret:
        return "empty"
    if secret.startswith(("sk_test_", "rk_test_")):
        return "test"
    if secret.startswith(("sk_live_", "rk_live_")):
        return "live"
    return "unknown"


def child_environment(overrides: dict[str, str]) -> dict[str, str]:
    environment = os.environ.copy()
    environment.update(overrides)
    return environment


def start_api(environment: dict[str, str]) -> int:
    command = [
        "dotnet",
        "run",
        "--project",
        str(repo_root() / "src" / "MaisonCalliard.Api" / "MaisonCalliard.Api.csproj"),
        "--launch-profile",
        "http",
    ]
    completed = subprocess.run(command, cwd=repo_root(), env=environment, check=False)
    return completed.returncode


def load_all() -> tuple[dict[str, str], dict[str, str]]:
    user_secrets = load_user_secrets()
    return user_secrets, merge_configuration(user_secrets)


def load_user_secrets() -> dict[str, str]:
    path = user_secrets_path()
    if not path.is_file():
        raise GuardFailure("User Secrets-filen saknas.")
    return flatten(read_json(path))


def merge_configuration(user_secrets: dict[str, str]) -> dict[str, str]:
    root = repo_root()
    merged = flatten(read_json(root / "src" / "MaisonCalliard.Api" / "appsettings.json"))
    development = root / "src" / "MaisonCalliard.Api" / "appsettings.Development.json"
    if development.is_file():
        merged.update(flatten(read_json(development)))
    merged.update(user_secrets)
    return merged


def user_secrets_path() -> Path:
    appdata = os.environ.get("APPDATA")
    if not appdata:
        raise GuardFailure("User Secrets kan inte lokaliseras.")
    return Path(appdata) / "Microsoft" / "UserSecrets" / USER_SECRETS_ID / "secrets.json"


def read_json(path: Path) -> object:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        raise GuardFailure(f"Konfigurationsfilen {path.name} kunde inte läsas.") from None


def flatten(value: object, prefix: str = "") -> dict[str, str]:
    flat: dict[str, str] = {}
    if isinstance(value, dict):
        for key, item in value.items():
            child = f"{prefix}:{key}" if prefix else str(key)
            flat.update(flatten(item, child))
        return flat
    if isinstance(value, list):
        for index, item in enumerate(value):
            flat.update(flatten(item, f"{prefix}:{index}"))
        return flat
    flat[prefix] = "" if value is None else str(value)
    return flat


def repo_root() -> Path:
    for parent in Path(__file__).resolve().parents:
        project = parent / "src" / "MaisonCalliard.Api" / "MaisonCalliard.Api.csproj"
        if project.is_file():
            return parent
    raise GuardFailure("Projektroten kunde inte hittas.")


def first(properties: dict[str, str], *names: str) -> str:
    for name in names:
        if name in properties:
            return properties[name]
    return ""


def normalize_ssl(value: str | None) -> str | None:
    if value is None or not value.strip():
        return None
    return re.sub(r"[\s_]", "", value).lower()


def is_true(value: str | None) -> bool:
    return (value or "").strip().lower() in {"true", "yes", "1"}


def is_placeholder_password(password: str) -> bool:
    normalized = password.strip().lower()
    return (
        normalized in PLACEHOLDER_PASSWORDS
        or "placeholder" in normalized
        or normalized.startswith("your_")
        or "changeme" in normalized
        or "change_me" in normalized
    )


def looks_like_sql_server(properties: dict[str, str], host: str) -> bool:
    return "initialcatalog" in properties or host.lower().endswith(".database.windows.net")


if __name__ == "__main__":
    sys.exit(main())
