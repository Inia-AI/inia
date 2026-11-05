#!/usr/bin/env python3
"""Compares the Core and UserData schemas of two databases on SQL Server and PostgreSQL, for a
migration squash: a database built from the old migrations against one built from the new ones.

  compare.py <old-database> <new-database>

The servers come from INIA_TEST_MSSQL and INIA_TEST_PG, the connection strings the database
integration tests read. A provider whose variable is unset is skipped. SQLCMD names the sqlcmd
command (default: sqlcmd), for example SQLCMD="docker exec -i -e SQLCMDPASSWORD <container>
/opt/mssql-tools18/bin/sqlcmd" for a SQL Server container. pg_dump must be on the path.

A column default that only the old database has is reported apart from the other differences:
an old AddColumn with defaultValue leaves one that the model does not declare. The exit code is 1
when any other difference is found, and 2 when nothing was compared or a command failed.
"""
import os
import re
import shlex
import subprocess
import sys

SCHEMAS = ("Core", "UserData")
HISTORY_TABLE = "__EFMigrationsHistory"
FIELD_SEPARATOR = "|"

MSSQL_SCHEMA_QUERY = f"""
set nocount on;
select 'TBL', s.name, t.name, '' from sys.tables t join sys.schemas s on t.schema_id = s.schema_id
where s.name in ('Core', 'UserData') and t.name <> '{HISTORY_TABLE}';
select 'COL', s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
  isnull(dc.definition, ''), isnull(cc.definition, ''), isnull(c.collation_name, '')
from sys.columns c join sys.tables t on c.object_id = t.object_id join sys.schemas s on t.schema_id = s.schema_id
join sys.types ty on c.user_type_id = ty.user_type_id
left join sys.default_constraints dc on dc.parent_object_id = c.object_id and dc.parent_column_id = c.column_id
left join sys.computed_columns cc on cc.object_id = c.object_id and cc.column_id = c.column_id
where s.name in ('Core', 'UserData') and t.name <> '{HISTORY_TABLE}';
select 'IDX', s.name, t.name, isnull(i.name, '(heap)'), i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint,
  isnull(i.filter_definition, ''),
  isnull(stuff((select ',' + c.name + case ic.is_descending_key when 1 then ' DESC' else '' end
    from sys.index_columns ic join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
    where ic.object_id = i.object_id and ic.index_id = i.index_id and ic.is_included_column = 0
    order by ic.key_ordinal for xml path('')), 1, 1, ''), ''),
  isnull(stuff((select ',' + c.name
    from sys.index_columns ic join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
    where ic.object_id = i.object_id and ic.index_id = i.index_id and ic.is_included_column = 1
    order by c.name for xml path('')), 1, 1, ''), '')
from sys.indexes i join sys.tables t on i.object_id = t.object_id join sys.schemas s on t.schema_id = s.schema_id
where s.name in ('Core', 'UserData') and t.name <> '{HISTORY_TABLE}';
select 'FK', s.name, t.name, fk.name, rs.name, rt.name, fk.delete_referential_action_desc, fk.update_referential_action_desc,
  stuff((select ',' + pc.name + '->' + rc.name from sys.foreign_key_columns fkc
    join sys.columns pc on pc.object_id = fkc.parent_object_id and pc.column_id = fkc.parent_column_id
    join sys.columns rc on rc.object_id = fkc.referenced_object_id and rc.column_id = fkc.referenced_column_id
    where fkc.constraint_object_id = fk.object_id order by fkc.constraint_column_id for xml path('')), 1, 1, '')
from sys.foreign_keys fk join sys.tables t on fk.parent_object_id = t.object_id join sys.schemas s on t.schema_id = s.schema_id
join sys.tables rt on fk.referenced_object_id = rt.object_id join sys.schemas rs on rt.schema_id = rs.schema_id
where s.name in ('Core', 'UserData');
select 'CK', s.name, t.name, ck.name, ck.definition
from sys.check_constraints ck join sys.tables t on ck.parent_object_id = t.object_id join sys.schemas s on t.schema_id = s.schema_id
where s.name in ('Core', 'UserData');
select 'OBJ', s.name, o.name, o.type_desc from sys.objects o join sys.schemas s on o.schema_id = s.schema_id
where s.name in ('Core', 'UserData') and o.type not in ('U', 'PK', 'F', 'C', 'D', 'UQ', 'IT', 'S');
"""

COLUMN_DEFAULT_FIELD = 6
CREATE_TABLE = re.compile(r'CREATE TABLE ("[^"]+"\."[^"]+") \((.*?)\n\);', re.S)
PG_COLUMN_DEFAULT = re.compile(r" DEFAULT .*? NOT NULL")


def parse_connection(connection):
    pairs = (part.split("=", 1) for part in connection.split(";") if "=" in part)
    return {key.strip().lower(): value.strip() for key, value in pairs}


def first(settings, *keys, default=None):
    for key in keys:
        if key in settings:
            return settings[key]
    return default


def mssql_rows(connection, database):
    settings = parse_connection(connection)
    command = shlex.split(os.environ.get("SQLCMD", "sqlcmd")) + [
        "-S", first(settings, "server", "data source"),
        "-U", first(settings, "user id", "uid", "user"),
        "-C", "-d", database, "-h", "-1", "-W", "-s", FIELD_SEPARATOR, "-b", "-Q", MSSQL_SCHEMA_QUERY,
    ]
    environment = dict(os.environ, SQLCMDPASSWORD=first(settings, "password", "pwd", default=""))
    output = run(command, environment)
    rows = {}
    for line in output.splitlines():
        fields = line.split(FIELD_SEPARATOR)
        if fields[0] == "COL":
            rows[tuple(fields[:4])] = fields[4:]
        elif fields[0] in ("TBL", "IDX", "FK", "CK", "OBJ"):
            rows[tuple(fields)] = None
    return rows


def compare_mssql(connection, old_database, new_database):
    old, new = mssql_rows(connection, old_database), mssql_rows(connection, new_database)
    defaults, others = [], []
    for key in sorted(set(old) | set(new)):
        if key not in old or key not in new:
            others.append(f"only in {'old' if key in old else 'new'}: {FIELD_SEPARATOR.join(key)}")
        elif old[key] != new[key]:
            before, after = old[key], new[key]
            same_except_default = (before[:COLUMN_DEFAULT_FIELD] == after[:COLUMN_DEFAULT_FIELD]
                                   and before[COLUMN_DEFAULT_FIELD + 1:] == after[COLUMN_DEFAULT_FIELD + 1:])
            if same_except_default and after[COLUMN_DEFAULT_FIELD] == "":
                defaults.append(f"{'.'.join(key[1:])} {before[COLUMN_DEFAULT_FIELD]}")
            else:
                others.append(f"changed: {FIELD_SEPARATOR.join(key)}: {before} -> {after}")
    tables = sum(1 for key in old if key[0] == "TBL")
    return tables, defaults, others


def pg_schema(connection, database):
    settings = parse_connection(connection)
    command = ["pg_dump", "--schema-only", "--no-owner", "--no-privileges",
               "-h", first(settings, "host", "server"), "-p", first(settings, "port", default="5432"),
               "-U", first(settings, "username", "user id", "user"), "-d", database]
    for schema in SCHEMAS:
        command += ["-n", f'"{schema}"']
    environment = dict(os.environ, PGPASSWORD=first(settings, "password", default=""))
    dump = run(command, environment)
    dump = "\n".join(line for line in dump.splitlines()
                     if line and not line.startswith(("--", "SET ", "SELECT pg_catalog")))
    columns = {}
    for table in CREATE_TABLE.finditer(dump):
        for line in table.group(2).split("\n"):
            column = line.strip().rstrip(",")
            if column:
                columns[(table.group(1), " ".join(column.split(" ")[:2 if column.startswith("CONSTRAINT ") else 1]))] = column
    statements = {statement.strip() for statement in CREATE_TABLE.sub("", dump).split(";") if statement.strip()}
    return columns, statements


def compare_pg(connection, old_database, new_database):
    (old_columns, old_statements), (new_columns, new_statements) = (
        pg_schema(connection, old_database), pg_schema(connection, new_database))
    defaults, others = [], []
    for key in sorted(set(old_columns) | set(new_columns)):
        before, after = old_columns.get(key), new_columns.get(key)
        if before == after:
            continue
        if before and after and PG_COLUMN_DEFAULT.sub(" NOT NULL", before) == after:
            defaults.append(f"{key[0]}.{key[1]} {before}")
        else:
            others.append(f"column {key[0]}.{key[1]}: {before} -> {after}")
    others += [f"only in old: {statement[:200]}" for statement in sorted(old_statements - new_statements)]
    others += [f"only in new: {statement[:200]}" for statement in sorted(new_statements - old_statements)]
    tables = len({table for table, _ in old_columns if not table.endswith(f'"{HISTORY_TABLE}"')})
    return tables, defaults, others


def fail(message):
    print(message, file=sys.stderr)
    sys.exit(2)


def run(command, environment):
    try:
        return subprocess.run(command, env=environment, check=True, capture_output=True, text=True).stdout
    except subprocess.CalledProcessError as failure:
        fail(f"{command[0]} failed with exit code {failure.returncode}:\n{failure.stderr or failure.stdout}")


def main():
    if len(sys.argv) != 3:
        fail(__doc__)
    old_database, new_database = sys.argv[1], sys.argv[2]
    found_other = False
    compared = False
    for provider, variable, compare in (("SQL Server", "INIA_TEST_MSSQL", compare_mssql),
                                        ("PostgreSQL", "INIA_TEST_PG", compare_pg)):
        connection = os.environ.get(variable)
        if not connection:
            print(f"{provider}: skipped, {variable} is not set")
            continue
        tables, defaults, others = compare(connection, old_database, new_database)
        compared = True
        print(f"{provider}: {tables} tables, {len(defaults)} column defaults only in the old database, "
              f"{len(others)} other differences")
        for line in defaults:
            print(f"  default only in old: {line}")
        for line in others:
            print(f"  {line}")
        found_other = found_other or bool(others)
    if not compared:
        fail("Nothing compared: set INIA_TEST_MSSQL, INIA_TEST_PG or both.")
    sys.exit(1 if found_other else 0)


if __name__ == "__main__":
    main()
