
echo "clean start"

remove () {
    echo "rm -rf $1"
    rm -rf "$1"
}

clear () {
    echo "cd $1"
    cd "$1"
    remove ./obj/
    remove ./bin/
    cd ..
}

go () {
    echo "clear $1"
    cd "$1"
    clear "$2"
    cd ..
}

remove ./KitX\ Publish/
remove ./KitX\ Build/
remove ./TestResults/

# KitX Contracts
go ./KitX\ Contracts/ KitX.Contract.CSharp

# KitX Dashboard Helper
go ./KitX\ Dashboard\ Helper KitX.Assets
go ./KitX\ Dashboard\ Helper KitX.Fonts
go ./KitX\ Dashboard\ Helper KitX.Updater

# KitX File Format Helper
go ./KitX\ File\ Format\ Helper KitX.KXP.Helper

# KitX Installer
cd ./KitX\ Installer
go ./Installer\ for\ Windows KitX\ Installer\ for\ Windows\ in\ .NET\ Framework
cd ..

# KitX Loaders
go ./KitX\ Loaders/ KitX.Loader.Winform.Core
go ./KitX\ Loaders/ KitX.Loader.Winform.Framework
go ./KitX\ Loaders/ KitX.Loader.WPF.Core
go ./KitX\ Loaders/ KitX.Loader.WPF.Framework

# KitX Official Plugins
go ./KitX\ Official\ Plugins/ TestPlugin.WPF.Core
go ./KitX\ Official\ Plugins/ TestPlugin.WPF.Framework

# KitX Rules
go ./KitX\ Rules/ KitX.Web.Rules

# KitX Tools
go ./KitX\ Tools KitX.Connector
go ./KitX\ Tools KitX.KXP.Tool
go ./KitX\ Tools KitX.Struct.Producer

# KitX Dashboard
clear KitX\ Dashboard


echo "done."
sleep 5

