param($type)

echo "KitX Repository Initialize Script"
echo "Last updated at: 2023.07.26 20:00"

echo ""
echo "Type: $type"

if($type -eq "list")
{
    echo "    - dashboard"
    echo "    - mobile"
    echo "    - loader"
    echo "    - plugin"
    echo "    - installer"
    echo "    - reference"
}

if($type -eq "dashboard")
{
    echo "    | KitX Dashboard"
    echo "    | KitX File Formats"
    echo "    | KitX Rules"
    echo "    \ KitX Script"
    echo ""
    echo "executing ..."

    git submodule update "KitX Clients/KitX Dashboard"
    cd "KitX Clients/KitX Dashboard"
    git checkout dev=main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX File Formats"
    cd "KitX Standard/KitX File Formats"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Rules"
    cd "KitX Standard/KitX Rules"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Script"
    cd "KitX Standard/KitX Script"
    git checkout main
    git pull
    cd "../.."

    echo ""
    echo "To develop <dashboard> sets, you need <reference> sets too."
}

if($type -eq "mobile")
{
    echo "    \ KitX Mobile"
    echo ""
    echo "executing ..."

    git submodule update "KitX Clients/KitX Mobile"
    cd "KitX Clients/KitX Mobile"
    git checkout dev=main
    git pull
    cd "../.."
}

if($type -eq "loader")
{
    echo "    | KitX Contracts"
    echo "    | KitX Loaders"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Standard/KitX Contracts"
    cd "KitX Standard/KitX Contracts"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Loaders"
    cd "KitX Standard/KitX Loaders"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Rules"
    cd "KitX Standard/KitX Rules"
    git checkout main
    git pull
    cd "../.."
}

if($type -eq "plugin")
{
    echo "    | KitX Contracts"
    echo "    | KitX Loaders"
    echo "    | KitX Plugins"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Standard/KitX Contracts"
    cd "KitX Standard/KitX Contracts"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Loaders"
    cd "KitX Standard/KitX Loaders"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Plugins"
    cd "KitX Standard/KitX Plugins"
    git checkout main
    git pull
    cd "../.."

    git submodule update "KitX Standard/KitX Rules"
    cd "KitX Standard/KitX Rules"
    git checkout main
    git pull
    cd "../.."
}

if($type -eq "installer")
{
    echo "    \ KitX Installer"
    echo ""
    echo "executing ..."

    git submodule update "KitX Clients/KitX Installer"
    cd "KitX Clients/KitX Installer"
    git checkout main
    git pull
    cd "../.."
}

if($type -eq "reference")
{
    echo "    | Reference/Common.Activity"
    echo "    | Reference/Common.Algorithm"
    echo "    | Reference/Common.BasicHelper"
    echo "    | Reference/Common.ExternalConsole"
    echo "    \ Reference/Common.Update"
    echo ""
    echo "executing ..."

    git submodule update "Reference/Common.Activity"
    cd "Reference/Common.Activity"
    git checkout dev=main
    git pull
    cd "../.."

    git submodule update "Reference/Common.Algorithm"
    cd "Reference/Common.Algorithm"
    git checkout dev=main
    git pull
    cd "../.."

    git submodule update "Reference/Common.BasicHelper"
    cd "Reference/Common.BasicHelper"
    git checkout dev=main
    git pull
    cd "../.."

    git submodule update "Reference/Common.ExternalConsole"
    cd "Reference/Common.ExternalConsole"
    git checkout dev=main
    git pull
    cd "../.."

    git submodule update "Reference/Common.Update"
    cd "Reference/Common.Update"
    git checkout main
    git pull
    cd "../.."
}


