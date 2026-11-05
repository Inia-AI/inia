#!/usr/bin/env zsh

# ==============================================================================
# Database Migration Utility for IniaAi Platform
# ==============================================================================
# This script manages database migrations for both UserData and Core databases.
# It supports MSSQL and PostgreSQL providers with consistent patterns for
# creating, applying, and rolling back migrations.
#
# Usage: source create_and_apply_migration.zsh

# Ensure script is sourced, not executed
if [[ "$0" != "${(%):-%N}" ]]; then
    echo "This script is intended to be sourced, not executed directly."
    return 1
fi

# Initialize environment
emulate -L zsh
setopt extended_glob

# Check if script is already loaded to prevent redefining read-only variables
if (( ${+INIAAI_MIGRATION_LOADED} )); then
    print_usage
    return 0
fi

# Mark script as loaded
export INIAAI_MIGRATION_LOADED=1

# Constants
typeset -r LOG_PREFIX="[IniaAi Migration]"
typeset -r SEPARATOR="==================================================================="
typeset -r WEBAPP_PATH="./IniaAi.WebApp"
typeset -A DB_PROVIDERS=([MSSQL]=Mssql [PostgreSQL]=PostgreSql)

# Terminal color definitions
typeset -r COLOR_RESET=$'\e[0m'
typeset -r COLOR_BLUE=$'\e[34m'
typeset -r COLOR_GREEN=$'\e[32m'
typeset -r COLOR_YELLOW=$'\e[33m'
typeset -r COLOR_CYAN=$'\e[36m'
typeset -r COLOR_MAGENTA=$'\e[35m'
typeset -r COLOR_RED=$'\e[31m'
typeset -r COLOR_BOLD=$'\e[1m'

# Logging functions
log_info() {
    echo "\n${LOG_PREFIX} ${COLOR_BLUE}INFO:${COLOR_RESET} $1\n"
}

log_success() {
    echo "\n${LOG_PREFIX} ${COLOR_GREEN}SUCCESS:${COLOR_RESET} $1\n"
}

log_error() {
    echo "\n${LOG_PREFIX} ${COLOR_RED}ERROR:${COLOR_RESET} $1\n" >&2
}

log_section() {
    echo "${COLOR_BOLD}${SEPARATOR}${COLOR_RESET}"
    log_info "$1"
    echo "${COLOR_BOLD}${SEPARATOR}${COLOR_RESET}"
}

# Validate input parameters
validate_db_provider() {
    local db_provider="$1"
    if [[ -z "${DB_PROVIDERS[$db_provider]}" ]]; then
        log_error "Invalid database provider: '$db_provider'. Must be MSSQL or PostgreSQL."
        return 1
    fi
    return 0
}

validate_migration_name() {
    local migration_name="$1"
    if [[ -z "$migration_name" ]]; then
        log_error "Migration name cannot be empty."
        return 1
    fi
    # Check for valid C# identifier (alphanumeric and underscore)
    if [[ ! "$migration_name" =~ ^[a-zA-Z][a-zA-Z0-9_]*$ ]]; then
        log_error "Migration name must be a valid C# identifier (start with letter, contain only letters, numbers, and underscores)."
        return 1
    fi
    return 0
}

# Environment management functions
setup_environment() {
    local db_provider="$1"
    export ASPNETCORE_ENVIRONMENT=LocalDevelopment
    export PlatformSettings__UserDataDbProvider="$db_provider"
    export PlatformSettings__CoreDbProvider="$db_provider"
    log_info "Environment configured for $db_provider"
}

cleanup_environment() {
    unset ASPNETCORE_ENVIRONMENT
    unset PlatformSettings__UserDataDbProvider
    unset PlatformSettings__CoreDbProvider
    log_info "Environment variables cleaned up"
}

# Migration functions
create_migration() {
    local db_provider="$1"
    local migration_name="$2"
    local context="$3"

    validate_db_provider "$db_provider" || return 1
    validate_migration_name "$migration_name" || return 1

    local project_suffix="${DB_PROVIDERS[$db_provider]}"
    local migration_project="./IniaAi.Migrations.$project_suffix"

    log_section "Creating ${context}DbContext migration for $db_provider with name $migration_name"

    dotnet ef migrations add "$migration_name" \
        -o "$context" \
        -c "${context}DbContext" \
        -s "$WEBAPP_PATH" \
        -p "$migration_project" \
        -v || {
        log_error "Failed to create migration for ${context}DbContext"
        return 1
    }

    log_success "Migration created for ${context}DbContext"
}

create_migration_for_all() {
    local migration_name="$1"
    local failures=0

    validate_migration_name "$migration_name" || return 1

    # Process MSSQL migrations
    setup_environment "MSSQL"
    create_migration "MSSQL" "$migration_name" "UserData" || failures=$((failures + 1))
    create_migration "MSSQL" "$migration_name" "Core" || failures=$((failures + 1))
    cleanup_environment

    # Process PostgreSQL migrations
    setup_environment "PostgreSQL"
    create_migration "PostgreSQL" "$migration_name" "UserData" || failures=$((failures + 1))
    create_migration "PostgreSQL" "$migration_name" "Core" || failures=$((failures + 1))
    cleanup_environment

    if (( failures > 0 )); then
        log_error "$failures of 4 migrations failed; read the errors above"
        return 1
    fi

    log_section "All migrations created successfully"
}

remove_migration() {
    local db_provider="$1"
    local context="$2"

    validate_db_provider "$db_provider" || return 1
    local project_suffix="${DB_PROVIDERS[$db_provider]}"

    log_section "Removing ${context}DbContext migration for $db_provider"

    dotnet ef migrations remove \
        -c "${context}DbContext" \
        -s "$WEBAPP_PATH" \
        -p "./IniaAi.Migrations.$project_suffix" || {
        log_error "Failed to remove migration for ${context}DbContext"
        return 1
    }

    log_success "Migration removed for ${context}DbContext"
}

remove_core_migration_for_all() {
    # Process MSSQL migrations
    setup_environment "MSSQL"
    remove_migration "MSSQL" "Core"
    cleanup_environment

    # Process PostgreSQL migrations
    setup_environment "PostgreSQL"
    remove_migration "PostgreSQL" "Core"
    cleanup_environment

    log_section "All migrations removed successfully"
}

remove_userdata_migration_for_all() {
    # Process MSSQL migrations
    setup_environment "MSSQL"
    remove_migration "MSSQL" "UserData"
    cleanup_environment

    # Process PostgreSQL migrations
    setup_environment "PostgreSQL"
    remove_migration "PostgreSQL" "UserData"
    cleanup_environment

    log_section "All migrations removed successfully"
}

remove_migration_for_all() {
    # Process MSSQL migrations
    setup_environment "MSSQL"
    remove_migration "MSSQL" "UserData"
    remove_migration "MSSQL" "Core"
    cleanup_environment

    # Process PostgreSQL migrations
    setup_environment "PostgreSQL"
    remove_migration "PostgreSQL" "UserData"
    remove_migration "PostgreSQL" "Core"
    cleanup_environment

    log_section "All migrations removed successfully"
}

update_database() {
    local db_provider="$1"
    local context="$2"

    validate_db_provider "$db_provider" || return 1

    log_section "Applying ${context}DbContext migration for $db_provider"

    dotnet ef database update \
        -c "${context}DbContext" \
        -s "$WEBAPP_PATH" || {
        log_error "Failed to update database for ${context}DbContext"
        return 1
    }

    log_success "Database updated for ${context}DbContext"
}

update_database_for_all() {
    # Process MSSQL migrations
    setup_environment "MSSQL"
    update_database "MSSQL" "UserData"
    update_database "MSSQL" "Core"
    cleanup_environment

    # Process PostgreSQL migrations
    setup_environment "PostgreSQL"
    update_database "PostgreSQL" "UserData"
    update_database "PostgreSQL" "Core"
    cleanup_environment

    log_section "All databases updated successfully"
}

downgrade_database() {
    local db_provider="$1"
    local schema_name="$2"
    local migration_name="$3"

    validate_db_provider "$db_provider" || return 1

    setup_environment "$db_provider"

    if [[ -z "$schema_name" ]]; then
        log_error "Schema name cannot be empty. Must be 'UserData' or 'Core'."
        return 1
    fi

    if [[ -z "$migration_name" ]]; then
        log_error "Migration name cannot be empty."
        return 1
    fi

    local project_suffix="${DB_PROVIDERS[$db_provider]}"

    log_section "Rolling back ${schema_name}DbContext migration for $db_provider to $migration_name"

    dotnet ef database update "$migration_name" \
        -c "${schema_name}DbContext" \
        -s "$WEBAPP_PATH/IniaAi.WebApp.csproj" \
        -p "./IniaAi.Migrations.$project_suffix" || {
        cleanup_environment
        log_error "Failed to downgrade database for ${schema_name}DbContext"
        return 1
    }

    cleanup_environment

    log_success "Database downgraded for ${schema_name}DbContext to $migration_name"
}

# Help and usage function
print_usage() {
    cat << EOF
${LOG_PREFIX} ${COLOR_BOLD}${COLOR_MAGENTA}Migration Utility${COLOR_RESET}

${COLOR_BOLD}Available functions:${COLOR_RESET}
  ${COLOR_CYAN}create_migration_for_all${COLOR_RESET} ${COLOR_YELLOW}<migration_name>${COLOR_RESET}
    Creates migrations for both UserData and Core databases for both providers.
    Example: ${COLOR_GREEN}create_migration_for_all "AddNewTable"${COLOR_RESET}

  ${COLOR_CYAN}create_migration${COLOR_RESET} ${COLOR_YELLOW}<db_provider> <migration_name> <context>${COLOR_RESET}
    Creates a migration for the specified database provider and context.
    Example: ${COLOR_GREEN}create_migration MSSQL "AddNewTable" "UserData"${COLOR_RESET}

  ${COLOR_CYAN}remove_core_migration_for_all${COLOR_RESET}
    Removes the latest migration for the Core database for both providers.
    Example: ${COLOR_GREEN}remove_core_migration_for_all${COLOR_RESET}

  ${COLOR_CYAN}remove_userdata_migration_for_all${COLOR_RESET}
    Removes the latest migration for the UserData database for both providers.
    Example: ${COLOR_GREEN}remove_userdata_migration_for_all${COLOR_RESET}

  ${COLOR_CYAN}remove_migration_for_all${COLOR_RESET}
    Removes the latest migration for both UserData and Core databases for both providers.

  ${COLOR_CYAN}remove_migration${COLOR_RESET} ${COLOR_YELLOW}<db_provider> <context>${COLOR_RESET}
    Removes the latest migration for the specified database provider and context.
    Example: ${COLOR_GREEN}remove_migration MSSQL "UserData"${COLOR_RESET}

  ${COLOR_CYAN}update_database_for_all${COLOR_RESET}
    Applies the latest migrations for both UserData and Core databases for both providers.

  ${COLOR_CYAN}update_database${COLOR_RESET} ${COLOR_YELLOW}<db_provider> <context>${COLOR_RESET}
    Applies the latest migrations for the specified database provider and context.
    Example: ${COLOR_GREEN}update_database MSSQL "UserData"${COLOR_RESET}

  ${COLOR_CYAN}downgrade_database${COLOR_RESET} ${COLOR_YELLOW}<db_provider> <schema_name> <migration_name>${COLOR_RESET}
    Downgrades the database to the specified migration.
    Example: ${COLOR_GREEN}downgrade_database MSSQL "UserData" "InitialMigration"${COLOR_RESET}
EOF
}

# Execute on script load
print_usage
