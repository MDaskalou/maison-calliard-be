import io
import os
import subprocess
import sys
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import dev_api_guard as guard
import reset_dev_admin_password as reset

DEV_REF = "devref00000000000000"
PROD_REF = "prodref0000000000000"
OTHER_REF = "otherref000000000000"
PASSWORD = "synthetic-password-not-real"
NEW_PASSWORD = "a-new-passphrase"


def connection(ref: str, password: str = PASSWORD, ssl: str = "Require") -> str:
    return ";".join([
        "Host=aws-0-eu-west-1.pooler.supabase.com",
        "Database=postgres",
        f"Username=postgres.{ref}",
        f"Password={password}",
        f"SSL Mode={ssl}",
    ])


def secrets(dev: str | None = None, prod: str | None = None) -> dict[str, str]:
    values: dict[str, str] = {}
    if prod is not None:
        values["ConnectionStrings:DefaultConnection"] = prod
    if dev is not None:
        values["ConnectionStrings:DevConnection"] = dev
    return values


def dev_secrets(dev_password: str = PASSWORD, prod_password: str = "another-synthetic-password") -> dict[str, str]:
    return secrets(connection(DEV_REF, dev_password), connection(PROD_REF, prod_password))


class ResetPlanTests(unittest.TestCase):
    def test_plan_accepts_only_the_verified_dev_connection(self) -> None:
        stored = dev_secrets()
        plan = reset.plan_reset(stored, {}, DEV_REF, reset.ALLOWED_EMAIL)

        self.assertTrue(plan.ok)
        self.assertEqual(plan.connection, stored["ConnectionStrings:DevConnection"])
        self.assertNotEqual(plan.connection, stored["ConnectionStrings:DefaultConnection"])
        self.assertEqual(plan.prod_ref, PROD_REF)
        self.assertNotIn(PASSWORD, "\n".join(plan.messages))
        self.assertNotIn("Host=", "\n".join(plan.messages))
        self.assertNotIn(PASSWORD, repr(plan))

    def test_expected_reference_is_not_taken_from_the_connection(self) -> None:
        plan = reset.plan_reset(dev_secrets(), {}, OTHER_REF, reset.ALLOWED_EMAIL)

        self.assertFalse(plan.ok)
        self.assertIsNone(plan.connection)
        self.assertIn("DevConnection matchar inte den angivna DEV-referensen.", plan.messages)

    def test_same_project_is_rejected(self) -> None:
        plan = reset.plan_reset(
            secrets(connection(DEV_REF), connection(DEV_REF, password="another-synthetic-password")),
            {},
            DEV_REF,
            reset.ALLOWED_EMAIL,
        )

        self.assertFalse(plan.ok)
        self.assertIsNone(plan.connection)
        self.assertIn("DEV och PROD är samma Supabase-projekt.", plan.messages)

    def test_other_email_is_rejected_before_the_guard_result_is_usable(self) -> None:
        plan = reset.plan_reset(dev_secrets(), {}, DEV_REF, "other@example.com")

        self.assertFalse(plan.ok)
        self.assertIsNone(plan.connection)
        self.assertEqual(plan.messages, ("Endast info@fuegodanceschool.se kan återställas.",))

    def test_unknown_stripe_secret_keeps_the_connection_unusable(self) -> None:
        plan = reset.plan_reset(dev_secrets(), {"Stripe:SecretKey": "not-a-classified-key"}, DEV_REF, reset.ALLOWED_EMAIL)

        self.assertFalse(plan.ok)
        self.assertIsNone(plan.connection)
        self.assertIn("En Stripe-hemlighet kan inte verifieras som testnyckel. Start avbryts.", plan.messages)

    def test_override_must_be_the_dev_connection_itself(self) -> None:
        stored = dev_secrets()
        fake = guard.GuardResult(True, (), {"ConnectionStrings__DefaultConnection": stored["ConnectionStrings:DefaultConnection"]})

        with mock.patch.object(guard, "evaluate", return_value=fake):
            plan = reset.plan_reset(stored, {}, DEV_REF, reset.ALLOWED_EMAIL)

        self.assertFalse(plan.ok)
        self.assertIsNone(plan.connection)
        self.assertIn("Processen kunde inte låsa anslutningen till DEV.", plan.messages)
        self.assertNotIn(PASSWORD, "\n".join(plan.messages))

    def test_uppercase_expected_reference_still_has_to_match_the_argument(self) -> None:
        plan = reset.plan_reset(dev_secrets(), {}, DEV_REF.upper(), reset.ALLOWED_EMAIL)

        self.assertTrue(plan.ok)
        self.assertEqual(plan.prod_ref, PROD_REF)


class ResetCommandTests(unittest.TestCase):
    def test_expected_dev_reference_is_required(self) -> None:
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as caught:
            reset.main([])

        self.assertNotEqual(caught.exception.code, 0)

    def test_other_email_does_not_load_secrets_or_connect(self) -> None:
        def loader():
            raise AssertionError("loaded")

        code, output = run_main(["--expected-dev-ref", DEV_REF, "--email", "other@example.com"], loader=loader)

        self.assertEqual(code, 1)
        self.assertIn("Endast info@fuegodanceschool.se kan återställas.", output)
        self.assertIn(reset.SECRETS_UNCHANGED, output)

    def test_email_must_be_selected_explicitly(self) -> None:
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as caught:
            reset.main(["--expected-dev-ref", DEV_REF, "--execute"])

        self.assertNotEqual(caught.exception.code, 0)

    def test_failed_guard_does_not_ask_for_a_password_or_connect(self) -> None:
        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            secrets(connection(DEV_REF, password=""), connection(PROD_REF)),
        )

        self.assertEqual(code, 1)
        self.assertIn("DevConnection saknar lösenord.", output)
        self.assertIn(reset.SECRETS_UNCHANGED, output)
        self.assertNotIn(PASSWORD, output)

    def test_default_and_dry_run_do_not_ask_for_a_password_or_connect(self) -> None:
        default_code, default_output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL],
            dev_secrets(),
        )
        dry_run_code, dry_run_output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--dry-run"],
            dev_secrets(),
        )

        self.assertEqual(default_code, 0)
        self.assertEqual(dry_run_code, 0)
        self.assertIn("Säkerhetskontrollerna passerade.", default_output)
        self.assertIn("Skrivskyddat läge: inget lösenord ändrades.", default_output)
        self.assertIn("Skrivskyddat läge: inget lösenord ändrades.", dry_run_output)
        self.assertIn(reset.SECRETS_UNCHANGED, default_output)
        self.assertNotIn("ConnectionStrings__DefaultConnection", default_output)
        self.assertNotIn(PASSWORD, default_output)
        self.assertNotIn("Host=", default_output)
        self.assertNotIn(PASSWORD, dry_run_output)

    def test_execute_and_dry_run_together_do_not_connect(self) -> None:
        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute", "--dry-run"],
            dev_secrets(),
        )

        self.assertEqual(code, 1)
        self.assertIn("Återställning och skrivskyddat läge kan inte kombineras. Inget ändrades.", output)

    def test_wrong_account_confirmation_does_not_connect(self) -> None:
        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            dev_secrets(),
            entry=reset.ResetEntry("other@example.com", NEW_PASSWORD, NEW_PASSWORD),
        )

        self.assertEqual(code, 1)
        self.assertIn("Det valda kontot bekräftades inte. Inget ändrades.", output)
        self.assertNotIn(NEW_PASSWORD, output)

    def test_password_mismatch_does_not_connect(self) -> None:
        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            dev_secrets(),
            passwords=("a-new-passphrase", "a-different-passphrase"),
        )

        self.assertEqual(code, 1)
        self.assertIn("Lösenorden matchar inte. Inget ändrades.", output)
        self.assertNotIn("a-new-passphrase", output)
        self.assertNotIn("a-different-passphrase", output)

    def test_weak_password_does_not_connect(self) -> None:
        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            dev_secrets(),
            passwords=("short-pass", "short-pass"),
        )

        self.assertEqual(code, 1)
        self.assertIn("Lösenordet uppfyller inte lösenordspolicyn. Inget ändrades.", output)

    def test_reset_uses_only_the_verified_dev_connection(self) -> None:
        stored = dev_secrets()
        snapshot = dict(stored)
        started: list[tuple[object, ...]] = []
        stdout = io.StringIO()

        with redirect_stdout(stdout):
            code = reset.main(
                ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
                loader=lambda: (stored, {}),
                confirmation_reader=lambda: reset.ResetEntry(reset.ALLOWED_EMAIL, NEW_PASSWORD, NEW_PASSWORD),
                executor=lambda *args: started.append(args) or 0,
            )

        output = stdout.getvalue()
        self.assertEqual(code, 0)
        self.assertEqual(stored, snapshot)
        self.assertEqual(len(started), 1)
        connection, expected, prod_ref, email, password = started[0]
        self.assertEqual(connection, stored["ConnectionStrings:DevConnection"])
        self.assertNotEqual(connection, stored["ConnectionStrings:DefaultConnection"])
        self.assertEqual(expected, DEV_REF)
        self.assertEqual(prod_ref, PROD_REF)
        self.assertEqual(email, reset.ALLOWED_EMAIL)
        self.assertEqual(password, NEW_PASSWORD)
        self.assertNotIn(NEW_PASSWORD, output)
        self.assertNotIn(PASSWORD, output)
        self.assertNotIn("Host=", output)
        self.assertNotIn("Password=", output)
        self.assertIn(reset.SECRETS_UNCHANGED, output)

    def test_execute_sends_the_exact_confirmed_password(self) -> None:
        password = "  a-new-pass\nphrase  "
        started: list[tuple[object, ...]] = []

        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            dev_secrets(),
            entry=reset.ResetEntry(reset.ALLOWED_EMAIL, password, password),
            executor=lambda *args: started.append(args) or 0,
        )

        self.assertEqual(code, 0)
        self.assertEqual(started[0][3], reset.ALLOWED_EMAIL)
        self.assertEqual(started[0][4], password)
        self.assertNotEqual(started[0][4], password.strip())
        self.assertNotIn(password, output)

    def test_executor_failure_does_not_print_the_password(self) -> None:
        def executor(*args):
            raise RuntimeError(args[0])

        code, output = run_main(
            ["--expected-dev-ref", DEV_REF, "--email", reset.ALLOWED_EMAIL, "--execute"],
            dev_secrets(),
            passwords=(NEW_PASSWORD, NEW_PASSWORD),
            executor=executor,
        )

        self.assertEqual(code, 1)
        self.assertIn("Återställningen avbröts. Inget ändrades.", output)
        self.assertNotIn(NEW_PASSWORD, output)
        self.assertNotIn(PASSWORD, output)
        self.assertNotIn("Host=", output)

    def test_sources_do_not_write_user_secrets(self) -> None:
        root = Path(__file__).resolve().parents[2]
        for relative in (
            "scripts/reset_dev_admin_password.py",
            "tools/DevAdminPasswordReset/Program.cs",
            "tools/DevAdminPasswordReset/DevAdminPasswordResetter.cs",
        ):
            source = (root / relative).read_text(encoding="utf-8")
            self.assertNotIn("user-secrets", source)
            self.assertNotIn("secrets.json", source)
            self.assertNotIn("write_text", source)

    def test_allowed_account_and_environment_name_match_the_reset_tool(self) -> None:
        source = (Path(__file__).resolve().parents[2] / "tools" / "DevAdminPasswordReset" / "DevAdminPasswordResetter.cs").read_text(encoding="utf-8")

        self.assertIn(f'AllowedEmail = "{reset.ALLOWED_EMAIL}"', source)
        self.assertIn(f'ConnectionEnvironmentVariable = "{reset.RESET_CONNECTION_ENV}"', source)


class LaunchTests(unittest.TestCase):
    def test_child_receives_only_the_verified_connection(self) -> None:
        calls: list[tuple[list[str], dict[str, object]]] = []
        prod = connection(PROD_REF, password="prod-secret-value")
        dev = connection(DEV_REF)

        def spawn(command, **kwargs):
            calls.append((list(command), kwargs))
            return subprocess.CompletedProcess(command, 0)

        with mock.patch.dict(os.environ, {"ConnectionStrings__DefaultConnection": prod, reset.RESET_CONNECTION_ENV: prod}, clear=False):
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                code = reset.launch_resetter(
                    dev,
                    DEV_REF,
                    PROD_REF,
                    reset.ALLOWED_EMAIL,
                    NEW_PASSWORD,
                    spawn=spawn,
                    dll_path=Path("reset.dll"),
                )
            self.assertEqual(os.environ["ConnectionStrings__DefaultConnection"], prod)
            self.assertEqual(os.environ[reset.RESET_CONNECTION_ENV], prod)

        self.assertEqual(code, 0)
        self.assertEqual(len(calls), 2)
        _, build_kwargs = calls[0]
        build_env = build_kwargs["env"]
        self.assertNotIn(reset.RESET_CONNECTION_ENV, build_env)
        self.assertNotIn("ConnectionStrings__DefaultConnection", build_env)
        self.assertTrue(all(dev not in str(value) for value in build_env.values()))
        run_command, run_kwargs = calls[1]
        self.assertEqual(run_kwargs["env"][reset.RESET_CONNECTION_ENV], dev)
        self.assertNotIn("ConnectionStrings__DefaultConnection", run_kwargs["env"])
        self.assertNotIn(prod, run_command)
        self.assertNotIn(dev, run_command)
        self.assertNotIn(NEW_PASSWORD, run_command)
        self.assertEqual(run_kwargs["input"], reset.password_payload(NEW_PASSWORD))
        self.assertNotIn("encoding", run_kwargs)
        self.assertNotIn(NEW_PASSWORD, stdout.getvalue())
        self.assertNotIn("prod-secret-value", stdout.getvalue())

    def test_other_email_is_not_spawned(self) -> None:
        def spawn(command, **kwargs):
            raise AssertionError("spawned")

        stdout = io.StringIO()
        with redirect_stdout(stdout):
            code = reset.launch_resetter("Host=prod.example;Password=secret", DEV_REF, PROD_REF, "other@example.com", NEW_PASSWORD, spawn=spawn)

        self.assertEqual(code, 1)
        self.assertIn("Endast info@fuegodanceschool.se kan återställas.", stdout.getvalue())
        self.assertNotIn("Host=", stdout.getvalue())
        self.assertNotIn("secret", stdout.getvalue())

    def test_failed_build_does_not_spawn_the_reset(self) -> None:
        calls: list[list[str]] = []

        def spawn(command, **kwargs):
            calls.append(list(command))
            return subprocess.CompletedProcess(command, 1)

        stdout = io.StringIO()
        with redirect_stdout(stdout):
            code = reset.launch_resetter(connection(DEV_REF), DEV_REF, PROD_REF, reset.ALLOWED_EMAIL, NEW_PASSWORD, spawn=spawn, dll_path=Path("reset.dll"))

        self.assertEqual(code, 1)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][1], "build")
        self.assertIn("Återställningsverktyget kunde inte byggas. Inget ändrades.", stdout.getvalue())
        self.assertNotIn(NEW_PASSWORD, stdout.getvalue())


class PasswordPolicyTests(unittest.TestCase):
    def test_policy_matches_the_admin_password_rules(self) -> None:
        self.assertFalse(reset.password_is_strong(""))
        self.assertFalse(reset.password_is_strong("short-pass"))
        self.assertFalse(reset.password_is_strong("   123456789"))
        self.assertFalse(reset.password_is_strong("x" * 257))
        self.assertTrue(reset.password_is_strong("a-valid-line\n"))
        self.assertTrue(reset.password_is_strong("  a-new-passphrase  "))
        self.assertTrue(reset.password_is_strong("a-new-pass\nphrase"))
        self.assertTrue(reset.password_is_strong("a-new-passphrase"))
        self.assertTrue(reset.password_is_strong("x" * 256))
        self.assertTrue(reset.password_is_strong("Ny-lösenord-åäö-1"))

    def test_payload_keeps_spaces_and_newlines(self) -> None:
        password = "  a-new-pass\nphrase  "
        payload = reset.password_payload(password)
        header, _, body = payload.partition(b"\n")

        self.assertEqual(int(header), len(password.encode("utf-8")))
        self.assertEqual(body.decode("utf-8"), password)
        self.assertNotEqual(body.decode("utf-8"), password.strip())


def run_main(
    argv: list[str],
    user_secrets: dict[str, str] | None = None,
    settings: dict[str, str] | None = None,
    passwords: tuple[str, str] | None = None,
    entry: reset.ResetEntry | None = None,
    loader=None,
    executor=None,
) -> tuple[int, str]:
    def default_loader():
        return user_secrets or {}, settings or {}

    def default_reader():
        if entry is not None:
            return entry
        if passwords is None:
            raise AssertionError("password requested")
        return reset.ResetEntry(reset.ALLOWED_EMAIL, passwords[0], passwords[1])

    def default_executor(*args):
        raise AssertionError("connected")

    stdout = io.StringIO()
    with redirect_stdout(stdout):
        code = reset.main(
            argv,
            loader=loader or default_loader,
            confirmation_reader=default_reader,
            executor=executor or default_executor,
        )
    return code, stdout.getvalue()


if __name__ == "__main__":
    unittest.main()
