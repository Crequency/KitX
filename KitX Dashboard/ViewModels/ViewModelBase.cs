using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#pragma warning disable CS0108 // 成员隐藏继承的成员；缺少关键字 new
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。

namespace KitX_Dashboard.ViewModels
{
    public class ViewModelBase : ReactiveObject
    {

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool RaiseAndSetIfChanged<T>(ref T field, T value,
            [CallerMemberName] string propertyName = "")
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
                return true;
            }
            return false;
        }
    }
}

#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning restore CS0108 // 成员隐藏继承的成员；缺少关键字 new

//                                                    _______
//                                              .,add88YYYYY88ba,
//                                         .,adPP""'         `"Yba___,aaadYPPba,
//                                     .,adP""                .adP""""'     .,Y8b
//                                  ,adP"'                __  d"'     .,ad8P""Y8I
//                               ,adP"'                  d88b I  .,adP""'   ,d8I'
//                             ,adP"                     Y8P" ,adP"'    .,adP"'
//                            adP"                        "' dP"     ,adP""'
//                         ,adP"                             P    ,adP"'
//                 .,,aaaad8P"                                 ,adP"
//            ,add88PP""""'                                  ,dP"
//         ,adP""'                                         ,dP"
//       ,8P"'                                            d8"
//     ,dP'                                              dP'
//     `"Yba                                             Y8
//       `"Yba                                           `8,
//         `"Yba,                                         8I
//            `"8b                                        8I
//              dP                              __       ,8I
//             ,8'                            ,d88b,    ,d8'
//             dP                           ,dP'  `Yb, ,d8'
//            ,8'                         ,dP"      `"Y8P'
//            dP                        ,8P"
//           ,8'                      ,dP"    Normand
//           dP                     ,dP"      Veilleux
//          ,8'                    ,8P'
//          I8                    dP"
//          IP                   dP'
//          dI                  dP'
//         ,8'                 dP'
//         dI                 dP'
//         8'                ,8'
//         8                ,8I
//         8                dP'
//         8               ,8'
//         8,              IP'
//         Ib             ,dI
//         `8             I8'
//          8,            8I
//          Yb            I8
//          `8,           I8
//           Yb           I8
//           `Y,          I8
//            Ib          I8,
//            `Ib         `8I
//             `8,         Yb
//              I8,        `8,
//              `Yb,        `8a
//               `Yb         `Yb,
//                I8          `Yb,
//                dP            `Yb,
//               ,8'              `Yb,
//               dP                 `Yb,
//              d88baaaad88ba,        `8,
//                 `"""'   `Y8ba,     ,dI
//                            `""Y8baadP'
