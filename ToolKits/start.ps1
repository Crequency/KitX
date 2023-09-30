param($type)

Write-Output "KitX Repository Initialize Script"
Write-Output "Last updated at: 2023.10.02 02:09"

Write-Output ""
Write-Output "Type: $type"

if($type -eq "list")
{
    Write-Output "    - dashboard"
    Write-Output "    - mobile"
    Write-Output "    - website"
    Write-Output "    - installer"
    Write-Output "    - loader"
    Write-Output "    - plugin"
    Write-Output "    - reference"
    Write-Output "    - all"
}

if($type -eq "dashboard")
{
    Write-Output "    | KitX Dashboard"
    Write-Output "    | KitX File Formats"
    Write-Output "    | KitX Rules"
    Write-Output "    \ KitX Script"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Clients/KitX Dashboard"
    Set-Location "KitX Clients/KitX Dashboard"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX File Formats"
    Set-Location "KitX Standard/KitX File Formats"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Rules"
    Set-Location "KitX Standard/KitX Rules"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Script"
    Set-Location "KitX Standard/KitX Script"
    git checkout dev=main
    git pull
    Set-Location "../.."

    Write-Output ""
    Write-Output "To develop <dashboard> sets, you need <reference> sets too."
}

if($type -eq "mobile")
{
    Write-Output "    \ KitX Mobile"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Clients/KitX Mobile"
    Set-Location "KitX Clients/KitX Mobile"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "website")
{
    Write-Output "    \ KitX Website"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Clients/KitX Website"
    Set-Location "KitX Clients/KitX Website"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "installer")
{
    Write-Output "    \ KitX Installer"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Clients/KitX Installer"
    Set-Location "KitX Clients/KitX Installer"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "loader")
{
    Write-Output "    | KitX Contracts"
    Write-Output "    | KitX Loaders"
    Write-Output "    \ KitX Rules"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Standard/KitX Contracts"
    Set-Location "KitX Standard/KitX Contracts"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Loaders"
    Set-Location "KitX Standard/KitX Loaders"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Rules"
    Set-Location "KitX Standard/KitX Rules"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "plugin")
{
    Write-Output "    | KitX Contracts"
    Write-Output "    | KitX Loaders"
    Write-Output "    | KitX Plugins"
    Write-Output "    \ KitX Rules"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "KitX Standard/KitX Contracts"
    Set-Location "KitX Standard/KitX Contracts"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Loaders"
    Set-Location "KitX Standard/KitX Loaders"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Plugins"
    Set-Location "KitX Standard/KitX Plugins"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "KitX Standard/KitX Rules"
    Set-Location "KitX Standard/KitX Rules"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "reference")
{
    Write-Output "    | Reference/Common.Activity"
    Write-Output "    | Reference/Common.Algorithm"
    Write-Output "    | Reference/Common.BasicHelper"
    Write-Output "    | Reference/Common.ExternalConsole"
    Write-Output "    \ Reference/Common.Update"
    Write-Output ""
    Write-Output "executing ..."

    git submodule update "Reference/Common.Activity"
    Set-Location "Reference/Common.Activity"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "Reference/Common.Algorithm"
    Set-Location "Reference/Common.Algorithm"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "Reference/Common.BasicHelper"
    Set-Location "Reference/Common.BasicHelper"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "Reference/Common.ExternalConsole"
    Set-Location "Reference/Common.ExternalConsole"
    git checkout dev=main
    git pull
    Set-Location "../.."

    git submodule update "Reference/Common.Update"
    Set-Location "Reference/Common.Update"
    git checkout dev=main
    git pull
    Set-Location "../.."
}

if($type -eq "all")
{
    Write-Output "    + All Submodules"
    Write-Output ""
    Write-Output "executing ..."

    git submodule

    git submodule update --recursive
}
