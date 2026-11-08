---
type: runbook
status: current
description: "Says what the platform does to its database each time it starts, and moves an install of the open repository from one release to the next: back up the database, check out the new release tag, rebuild, check the migration, and roll back by restoring the backup. Read when you upgrade an install, plan a rollback, or want to know why a start changed the database."
---

# Upgrade to a New Release

Each release of the open repository is a tag, such as `v2.2.12`, and the stack builds from the tag you check out. The data stays in the `postgres-data` volume of `compose.yaml` across an upgrade. The commands on this page run in the root folder of your clone, the folder that holds `compose.yaml`.

## What a start does to the database

The API and the admin app, the `api` and `webapp` services, each bring the database up to their release when they start. Each takes a lock in the database first, so only one of them changes it at a time, and the other waits for it. The platform keeps two schemas, `Core` and `UserData`, and the log names each one.

- When the database does not exist, the service creates it and applies every migration.
- When the release holds migrations that the database has not applied, the service applies them. The log says `pending migrations detected`, then `Pending migrations applied`.
- When the history is complete but a table is missing, the service applies the migrations again.
- Otherwise the log says `database is up to date`.

A migration that fails stops the service, and its log names the error. No start undoes a migration, so the way back to an older release is the backup you take before the upgrade.

## Upgrade

1. Back up the database. Keep the file until the new release has run for a while.

   ```bash
   docker compose exec -T postgres pg_dump -U inia -Fc inia > inia-before-upgrade.dump
   ```

2. Fetch the release and check it out. Use the tag of the release you want.

   ```bash
   git fetch --tags
   git checkout v2.2.12
   ```

3. Look for new settings between your release and the new one, and add any value you need to `.env`:

   ```bash
   git diff v2.2.11 v2.2.12 -- .env.example
   ```

4. Build and start the new release. The command returns when every service is healthy.

   ```bash
   docker compose up --build --wait
   ```

5. Read what the start did to the database:

   ```bash
   docker compose logs api webapp | grep -i -E 'migrations|up to date'
   ```

## Roll back

A rollback restores the database as it was before the upgrade. Everything written after the backup is lost.

1. Stop the two services that use the database:

   ```bash
   docker compose stop api webapp
   ```

2. Restore the backup:

   ```bash
   docker compose exec -T postgres pg_restore -U inia -d inia --clean --if-exists < inia-before-upgrade.dump
   ```

3. Check out the release you came from, and start it:

   ```bash
   git checkout v2.2.11
   docker compose up --build --wait
   ```

## Done when

- `git describe --tags` prints the release you upgraded to.
- `docker compose ps` shows every service `healthy`.
- `docker compose logs api webapp | grep -i -E 'migrations applied|up to date'` prints a line for `Core` and a line for `UserData`.

## Related pages

- [Quickstart](quickstart.md): the first start of the stack.
- [The Docker Stack](docker-stack.md): the services and volumes of `compose.yaml`.
- [Database Migrations](database-migrations.md): how a migration is made, and how a squash moves the history.
- [Troubleshooting](troubleshooting.md): failures a start or a run has met, with their fixes.
