#!/bin/bash

rm -rf ./KitX\ Publish/
rm -rf ./KitX\ Build/

# Clear obj & bin

# KitX Contracts
cd ./KitX\ Contracts/

cd ./KitX.Contract.CSharp/
rm -rf ./obj/ ./bin/

cd ../../

# KitX Dashboard
cd ./KitX\ Dashboard/
rm -rf ./obj/ ./bin/
cd ../

# KitX Loaders
cd ./KitX\ Loaders/

cd ./KitX.Loader.MSVC.Windows/
cd ../

cd ./KitX.Loader.Winform.Core/
rm -rf ./obj/ ./bin/
cd ../

cd ./KitX.Loader.WPF.Core/
rm -rf ./obj/ ./bin/
cd ../

cd ./KitX.Loader.WPF.Framework/
rm -rf ./obj/ ./bin/
cd ../

cd ../

# KitX Official Plugins
cd ./KitX\ Official\ Plugins/

cd ./TestPlugin.WPF.Core/
rm -rf ./obj/ ./bin/
cd ../

cd ../

# KitX Rules
cd ./KitX\ Rules/

cd ./KitX.Web.Rules/
rm -rf ./obj/ ./bin/
cd ../

cd ../



echo "done."
sleep 5

