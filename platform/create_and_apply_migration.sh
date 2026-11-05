#!/usr/bin/env bash

# This script creates and applies a migration for both UserData and Core databases.
# It takes a migration name as an argument and creates a migration with that name.
# It then applies the migration to both UserData and Core databases.
# To source this script, first make it executable by running `chmod +x create_and_apply_migration.sh`.
# Then, you can source it by running `source create_and_apply_migration.sh`.
# Usage: create_and_apply_migration <migration_name>
# Example: create_and_apply_migration "AddNewTable"

create_and_apply_migration() {
    local migration_name="$1"

    # Create migration
    create_migration MSSQL "$migration_name"
    create_migration PostgreSQL "$migration_name"

    # Apply migration
    update_database MSSQL
    update_database PostgreSQL

    unset PlatformSettings__UserDataDbProvider
    unset PlatformSettings__CoreDbProvider

    echo "==================================================================="
    echo -e "\nDONE\n"
    echo "==================================================================="
}

create_migration_for_all() {
    local migration_name="$1"
    local failed=0

    # Create migration
    create_migration MSSQL "$migration_name" || failed=1
    create_migration PostgreSQL "$migration_name" || failed=1

    unset PlatformSettings__UserDataDbProvider
    unset PlatformSettings__CoreDbProvider

    echo "==================================================================="
    if [[ "$failed" -ne 0 ]]; then
        echo -e "\nFAILED: a migration was not created; read the errors above\n"
        echo "==================================================================="
        return 1
    fi
    echo -e "\nDONE\n"
    echo "==================================================================="
}

create_migration() {
    local db_provider="$1"
    local migration_name="$2"

    if [[ "$db_provider" != "MSSQL" && "$db_provider" != "PostgreSQL" ]]; then
        echo "Invalid DbProvider. Use MSSQL or PostgreSQL."
        return 1
    fi

    export PlatformSettings__UserDataDbProvider="$db_provider"
    export PlatformSettings__CoreDbProvider="$db_provider"

    local project_suffix
    if [[ "$db_provider" == "MSSQL" ]]; then
        project_suffix="Mssql"
    else
        project_suffix="PostgreSql"
    fi

    echo "==================================================================="
    echo -e "\nCreating UserDataDbContext migration for $db_provider with name $migration_name\n"
    echo "==================================================================="
    dotnet-ef migrations add "$migration_name" -o UserData -c UserDataDbContext -s ./IniaAi.WebApp -p "./IniaAi.Migrations.$project_suffix" || return 1

    echo "==================================================================="
    echo -e "\nCreating CoreDbContext migration for $db_provider with name $migration_name\n"
    echo "==================================================================="
    dotnet-ef migrations add "$migration_name" -o Core -c CoreDbContext -s ./IniaAi.WebApp -p "./IniaAi.Migrations.$project_suffix" || return 1
}

update_database_for_all() {
    update_database MSSQL
    update_database PostgreSQL

    unset PlatformSettings__UserDataDbProvider
    unset PlatformSettings__CoreDbProvider

    echo "==================================================================="
    echo -e "\nDONE\n"
    echo "==================================================================="
}

update_database() {
    local db_provider="$1"

    if [[ "$db_provider" != "MSSQL" && "$db_provider" != "PostgreSQL" ]]; then
        echo "Invalid DbProvider. Use MSSQL or PostgreSQL."
        return 1
    fi

    export PlatformSettings__UserDataDbProvider="$db_provider"
    export PlatformSettings__CoreDbProvider="$db_provider"

    echo "==================================================================="
    echo -e "\nApplying UserDataDbContext migration for $db_provider\n"
    echo "==================================================================="
    dotnet-ef database update -c UserDataDbContext -s ./IniaAi.WebApp

    echo "==================================================================="
    echo -e "\nApplying CoreDbContext migration for $db_provider\n"
    echo "==================================================================="
    dotnet-ef database update -c CoreDbContext -s ./IniaAi.WebApp
}

remove_migration_for_all() {
    remove_migration MSSQL
    remove_migration PostgreSQL

    unset PlatformSettings__UserDataDbProvider
    unset PlatformSettings__CoreDbProvider

    echo "==================================================================="
    echo -e "\nDONE\n"
    echo "==================================================================="
}

remove_migration() {
    local db_provider="$1"
    
    if [[ "$db_provider" != "MSSQL" && "$db_provider" != "PostgreSQL" ]]; then
        echo "Invalid DbProvider. Use MSSQL or PostgreSQL."
        return 1
    fi

    export PlatformSettings__UserDataDbProvider="$db_provider"
    export PlatformSettings__CoreDbProvider="$db_provider"

    echo "==================================================================="
    echo -e "\nRemoving UserDataDbContext migration\n"
    echo "==================================================================="
    dotnet-ef migrations remove -c UserDataDbContext -s ./IniaAi.WebApp -p "./IniaAi.Migrations.$db_provider"

    echo "==================================================================="
    echo -e "\nRemoving CoreDbContext migration\n"
    echo "==================================================================="
    dotnet-ef migrations remove -c CoreDbContext -s ./IniaAi.WebApp -p "./IniaAi.Migrations.$db_provider"
}
