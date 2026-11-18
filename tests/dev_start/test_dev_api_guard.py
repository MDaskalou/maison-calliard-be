import io
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import dev_api_guard as guard

DEV_REF = "devref00000000000000"
PROD_REF = "prodref0000000000000"
OTHER_REF = "otherref000000000000"
PASSWORD = "synthetic-password-not-real"


def connection(ref: str, password: str = PASSWORD, ssl: str = "Require", trust: str | None = None, username: str | None = None) -> str:
    user = username if username is not None else f"postgres.{ref}"
    parts = [
        "Host=aws-0-eu-west-1.pooler.supabase.com",
        "Database=postgres",
        f"Username={user}",
        f"Password={password}",
    ]
    if ssl is not None:
        parts.append(f"SSL Mode={ssl}")
    if trust is not None:
        parts.append(f"Trust Server Certificate={trust}")
    return ";".join(parts)


def secrets(dev: str | None, prod: str | None = None) -> dict[str, str]:
    values: dict[str, str] = {}
    if prod is not None:
        values["ConnectionStrings:DefaultConnection"] = prod
    if dev is not None:
        values["ConnectionStrings:DevConnection"] = dev
    return values


def approved(dev: str | None = None, prod: str | None = None, expected: str = DEV_REF, settings: dict[str, str] | None = None):
    return guard.evaluate(
        secrets(connection(DEV_REF) if dev is None else dev, connection(PROD_REF) if prod is None else prod),
        settings or {},
        expected,
    )


class DevApiGuardTests(unittest.TestCase):
    def test_correct_dev_project_is_accepted(self) -> None:
        result = approved()

        self.assertTrue(result.ok)
        self.assertEqual(result.overrides["ConnectionStrings__DefaultConnection"], connection(DEV_REF))
        self.assertNotEqual(result.overrides["ConnectionStrings__DefaultConnection"], connection(PROD_REF))
        self.assertEqual(result.overrides["ASPNETCORE_ENVIRONMENT"], "Development")
        self.assertEqual(result.overrides["Cors__AllowedOriginsCsv"], "http://localhost:3031")
        self.assertEqual(result.overrides["Auth__AllowedBrowserOriginsCsv"], "http://localhost:3031")

    def test_same_dev_and_prod_project_is_rejected(self) -> None:
        result = approved(prod=connection(DEV_REF, password="another-synthetic-password"))

        self.assertFalse(result.ok)
        self.assertIsNone(result.overrides)
        self.assertIn("DEV och PROD är samma Supabase-projekt.", result.messages)

    def test_wrong_expected_dev_reference_is_rejected(self) -> None:
        result = approved(expected=OTHER_REF)

        self.assertFalse(result.ok)
        self.assertIn("DevConnection matchar inte den angivna DEV-referensen.", result.messages)

    def test_missing_dev_connection_is_rejected(self) -> None:
        result = guard.evaluate(secrets(None, connection(PROD_REF)), {}, DEV_REF)

        self.assertFalse(result.ok)
        self.assertIn("DevConnection saknas i User Secrets.", result.messages)

    def test_missing_password_is_rejected(self) -> None:
        result = approved(dev=connection(DEV_REF, password=""))

        self.assertFalse(result.ok)
        self.assertIn("DevConnection saknar lösenord.", result.messages)

    def test_placeholder_password_is_rejected(self) -> None:
        result = approved(dev=connection(DEV_REF, password="YOUR_LOCAL_PASSWORD_HERE"))

        self.assertFalse(result.ok)
        self.assertIn("DevConnection använder ett platshållarlösenord.", result.messages)

    def test_quoted_supabase_username_is_accepted(self) -> None:
        quoted = (
            "Host=aws-0-eu-west-1.pooler.supabase.com;Database=postgres;"
            f"Username='postgres.{DEV_REF}';Password='{PASSWORD}';SSL Mode=Require"
        )
        result = approved(dev=quoted)

        self.assertTrue(result.ok)
        self.assertEqual(result.overrides["ConnectionStrings__DefaultConnection"], quoted)

    def test_invalid_connection_string_is_rejected(self) -> None:
        result = approved(dev="Password='unterminated")

        self.assertFalse(result.ok)
        self.assertIn("DevConnection är inte en giltig PostgreSQL-anslutningssträng.", result.messages)

    def test_missing_or_incorrect_tls_settings_are_rejected(self) -> None:
        missing = approved(dev=connection(DEV_REF, ssl=None))
        disabled = approved(dev=connection(DEV_REF, ssl="Disable"))
        trusted = approved(dev=connection(DEV_REF, trust="true"))

        self.assertIn("DevConnection kräver inte TLS.", missing.messages)
        self.assertIn("DevConnection kräver inte TLS.", disabled.messages)
        self.assertIn("DevConnection stänger av certifikatkontrollen.", trusted.messages)

    def test_messages_do_not_contain_secrets(self) -> None:
        cases = [
            approved(),
            approved(dev=connection(DEV_REF, password="YOUR_LOCAL_PASSWORD_HERE")),
            approved(dev="Server=tcp:prod.database.windows.net;Database=prod;Password=" + PASSWORD),
            approved(dev=connection(DEV_REF), settings={"Stripe:SecretKey": "sk_live_" + PASSWORD}),
        ]

        for result in cases:
            rendered = "\n".join(result.messages)
            self.assertNotIn(PASSWORD, rendered)
            self.assertNotIn("sk_live_", rendered)
            self.assertNotIn("Username=", rendered)
            self.assertNotIn("Host=", rendered)

    def test_rejected_check_does_not_start_the_api(self) -> None:
        started: list[dict[str, str]] = []

        with redirect_stdout(io.StringIO()):
            code = run_main(
                ["--expected-dev-ref", DEV_REF],
                secrets(connection(DEV_REF, password=""), connection(PROD_REF)),
                started,
            )

        self.assertEqual(code, 1)
        self.assertEqual(started, [])

    def test_dry_run_does_not_start_the_api(self) -> None:
        started: list[dict[str, str]] = []
        stdout = io.StringIO()

        with redirect_stdout(stdout):
            code = run_main(
                ["--expected-dev-ref", DEV_REF, "--dry-run"],
                secrets(connection(DEV_REF), connection(PROD_REF)),
                started,
            )

        self.assertEqual(code, 0)
        self.assertEqual(started, [])
        self.assertIn("Dry-run: ingen server startades.", stdout.getvalue())
        self.assertNotIn(PASSWORD, stdout.getvalue())

    def test_unknown_supabase_project_refuses_startup(self) -> None:
        result = approved(settings={
            "Supabase:Url": f"https://{OTHER_REF}.supabase.co",
            "Supabase:ServiceRoleKey": "synthetic-service-role",
            "Supabase:StorageBucket": "uploads",
        })

        self.assertFalse(result.ok)
        self.assertIsNone(result.overrides)
        self.assertIn(
            "Supabase:Url pekar på ett projekt som varken är angiven DEV eller identifierad PROD.",
            result.messages,
        )

    def test_prod_supabase_storage_and_live_stripe_are_isolated(self) -> None:
        result = approved(settings={
            "Supabase:Url": f"https://{PROD_REF}.supabase.co",
            "Supabase:ServiceRoleKey": "synthetic-service-role",
            "Supabase:StorageBucket": "uploads",
            "Stripe:SecretKey": "sk_live_synthetic",
            "Stripe:Locations:Molndal:SecretKey": "sk_test_synthetic",
            "Resend:Enabled": "true",
            "Resend:ApiKey": "synthetic-resend-key",
        })

        self.assertTrue(result.ok)
        self.assertEqual(result.overrides["Supabase__ServiceRoleKey"], "")
        self.assertEqual(result.overrides["Stripe__SecretKey"], "")
        self.assertNotIn("Stripe__Locations__Molndal__SecretKey", result.overrides)
        self.assertEqual(result.overrides["Resend__Enabled"], "false")
        rendered = "\n".join(result.messages)
        self.assertNotIn("synthetic-service-role", rendered)
        self.assertNotIn("synthetic-resend-key", rendered)

    def test_unknown_stripe_secret_refuses_startup(self) -> None:
        result = approved(settings={"Stripe:SecretKey": "not-a-classified-key"})

        self.assertFalse(result.ok)
        self.assertIsNone(result.overrides)
        self.assertIn("En Stripe-hemlighet kan inte verifieras som testnyckel. Start avbryts.", result.messages)

    def test_child_environment_replaces_a_prod_value_already_in_the_process(self) -> None:
        result = approved()
        parent = {
            "ConnectionStrings__DefaultConnection": connection(PROD_REF),
            "DEV_GUARD_MARKER": "unchanged",
        }

        with mock.patch.dict("os.environ", parent, clear=True):
            environment = guard.child_environment(result.overrides)

        self.assertEqual(environment["ConnectionStrings__DefaultConnection"], connection(DEV_REF))
        self.assertEqual(environment["DEV_GUARD_MARKER"], "unchanged")


def run_main(argv: list[str], user_secrets: dict[str, str], started: list[dict[str, str]]) -> int:
    def starter(environment: dict[str, str]) -> int:
        started.append(environment)
        return 0

    return guard.main(argv, starter=starter, loader=lambda: (user_secrets, {}))


if __name__ == "__main__":
    unittest.main()
