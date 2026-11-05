# This script creates and applies a migration for both UserData and Core databases.
# It takes a migration name as an argument and creates a migration with that name.
# It then applies the migration to both UserData and Core databases.
# Usage: . .\create_and_apply_migration.ps1
# Then: Create-And-Apply-Migration -MigrationName <migration_name>
# Example: Create-And-Apply-Migration -MigrationName "AddNewTable"

function Create-And-Apply-Migration {
    param (
        [Parameter(Mandatory=$true)]
        [string]$MigrationName
    )

    # Create migration
    Create-Migration -DbProvider "MSSQL" -MigrationName $MigrationName
    Create-Migration -DbProvider "PostgreSQL" -MigrationName $MigrationName

    # Apply migration
    Update-Database -DbProvider "MSSQL"
    Update-Database -DbProvider "PostgreSQL"

    Remove-Item Env:\PlatformSettings__UserDataDbProvider -ErrorAction SilentlyContinue
    Remove-Item Env:\PlatformSettings__CoreDbProvider -ErrorAction SilentlyContinue

    Write-Host "===================================================================" -ForegroundColor Green
    Write-Host "`nDONE`n" -ForegroundColor Green
    Write-Host "===================================================================" -ForegroundColor Green
}

function Create-Migration-For-All {
    param (
        [Parameter(Mandatory=$true)]
        [string]$MigrationName
    )

    # Create migration
    Create-Migration -DbProvider "MSSQL" -MigrationName $MigrationName
    Create-Migration -DbProvider "PostgreSQL" -MigrationName $MigrationName

    Remove-Item Env:\PlatformSettings__UserDataDbProvider -ErrorAction SilentlyContinue
    Remove-Item Env:\PlatformSettings__CoreDbProvider -ErrorAction SilentlyContinue

    Write-Host "===================================================================" -ForegroundColor Green
    Write-Host "`nDONE`n" -ForegroundColor Green
    Write-Host "===================================================================" -ForegroundColor Green
}

function Create-Migration {
    param (
        [Parameter(Mandatory=$true)]
        [ValidateSet("MSSQL", "PostgreSQL")]
        [string]$DbProvider,
        [Parameter(Mandatory=$true)]
        [string]$MigrationName
    )

    $Env:PlatformSettings__UserDataDbProvider = $DbProvider
    $Env:PlatformSettings__CoreDbProvider = $DbProvider

    $projectSuffix = if ($DbProvider -eq "MSSQL") { "Mssql" } else { "PostgreSql" }

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nCreating UserDataDbContext migration for $DbProvider with name $MigrationName`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef migrations add $MigrationName --output-dir UserData --context UserDataDbContext --startup-project ./IniaAi.WebApp --project "./IniaAi.Migrations.$projectSuffix"

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nCreating CoreDbContext migration for $DbProvider with name $MigrationName`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef migrations add $MigrationName --output-dir Core --context CoreDbContext --startup-project ./IniaAi.WebApp --project "./IniaAi.Migrations.$projectSuffix"
}

function Update-Database-For-All {
    Update-Database -DbProvider "MSSQL"
    Update-Database -DbProvider "PostgreSQL"

    Remove-Item Env:\PlatformSettings__UserDataDbProvider -ErrorAction SilentlyContinue
    Remove-Item Env:\PlatformSettings__CoreDbProvider -ErrorAction SilentlyContinue

    Write-Host "===================================================================" -ForegroundColor Green
    Write-Host "`nDONE`n" -ForegroundColor Green
    Write-Host "===================================================================" -ForegroundColor Green
}

function Update-Database {
    param (
        [Parameter(Mandatory=$true)]
        [ValidateSet("MSSQL", "PostgreSQL")]
        [string]$DbProvider
    )

    $Env:PlatformSettings__UserDataDbProvider = $DbProvider
    $Env:PlatformSettings__CoreDbProvider = $DbProvider

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nApplying UserDataDbContext migration for $DbProvider`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef database update --context UserDataDbContext --startup-project ./IniaAi.WebApp

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nApplying CoreDbContext migration for $DbProvider`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef database update --context CoreDbContext --startup-project ./IniaAi.WebApp
}

function Remove-Migration-For-All {
    Remove-Migration -DbProvider "MSSQL"
    Remove-Migration -DbProvider "PostgreSQL"

    Remove-Item Env:\PlatformSettings__UserDataDbProvider -ErrorAction SilentlyContinue
    Remove-Item Env:\PlatformSettings__CoreDbProvider -ErrorAction SilentlyContinue

    Write-Host "===================================================================" -ForegroundColor Green
    Write-Host "`nDONE`n" -ForegroundColor Green
    Write-Host "===================================================================" -ForegroundColor Green
}

function Remove-Migration {
    param (
        [Parameter(Mandatory=$true)]
        [ValidateSet("MSSQL", "PostgreSQL")]
        [string]$DbProvider
    )

    $Env:PlatformSettings__UserDataDbProvider = $DbProvider
    $Env:PlatformSettings__CoreDbProvider = $DbProvider

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nRemoving UserDataDbContext migration`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef migrations remove --context UserDataDbContext --startup-project ./IniaAi.WebApp --project "./IniaAi.Migrations.$DbProvider"

    Write-Host "===================================================================" -ForegroundColor Cyan
    Write-Host "`nRemoving CoreDbContext migration`n" -ForegroundColor Cyan
    Write-Host "===================================================================" -ForegroundColor Cyan
    dotnet ef migrations remove --context CoreDbContext --startup-project ./IniaAi.WebApp --project "./IniaAi.Migrations.$DbProvider"
}
