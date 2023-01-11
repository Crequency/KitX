param($type)

echo "Type: $type"

if($type -eq "dashboard"){
    echo "    | KitX Dashboard"
    echo "    | KitX Dashboard Helper"
    echo "    | KitX File Format Helper"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Dashboard"
    git submodule update "KitX Dashboard Helper"
    git submodule update "KitX File Format Helper"
    git submodule update "KitX Rules"
}

if($type -eq "mobile"){
    echo "    \ KitX Mobile"
    echo ""
    echo "executing ..."

    git submodule update "KitX Mobile"
}

if($type -eq "loader"){
    echo "    | KitX Contracts"
    echo "    | KitX Loaders"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Contracts"
    git submodule update "KitX Loaders"
    git submodule update "KitX Rules"
}

if($type -eq "plugin"){
    echo "    | KitX Contracts"
    echo "    | KitX Loaders"
    echo "    | KitX Plugins"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Contracts"
    git submodule update "KitX Loaders"
    git submodule update "KitX Plugins"
    git submodule update "KitX Rules"
}

if($type -eq "installer"){
    echo "    \ KitX Installer"
    echo ""
    echo "executing ..."

    git submodule update "KitX Installer"
}


