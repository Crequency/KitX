# Error Codes | 错误代码及其定义

## 1001 - StartUp Arguments Error - 启动参数错误
一般是由于缺失必要参数, 比如使用 --import-plugin 参数之后你又没有传入 .kxp 文件的路径  
也有可能是内部逻辑错误, 可以检查程序根目录下的 dump.log 来查看异常退出原因

## 1002 - Config file didn't found, and process is not in Init field - 找不到配置文件, 而且当前进程并不处于初始化阶段.
一般是发生在使用 --import-plugin 参数导入插件且从未在本机启动过 KitX Dashboard 的情况  
由于配置文件会在第一次启动时生成, 此时导入插件辣就是纯纯出错, 而且不打算为这个错误准备修复预案

