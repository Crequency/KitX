#!/bin/bash

echo "Type: $1"

if [ $1 = "dashboard" ];
then

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

fi

if [ $1 = "mobile" ];
then
    echo "    \ KitX Mobile"
    echo ""
    echo "executing ..."

    git submodule update "KitX Mobile"
fi

if [ $1 = "loader" ];
then
    echo "    | KitX Contracts"
    echo "    | KitX Loaders"
    echo "    \ KitX Rules"
    echo ""
    echo "executing ..."

    git submodule update "KitX Contracts"
    git submodule update "KitX Loaders"
    git submodule update "KitX Rules"
fi

if [ $1 = "plugin" ];
then
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
fi

if [ $1 = "installer" ];
then
    echo "    \ KitX Installer"
    echo ""
    echo "executing ..."

    git submodule update "KitX Installer"
fi

sleep 3
