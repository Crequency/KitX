using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KitX_Dashboard.Data
{
    /// <summary>
    /// 市场配置文件
    /// </summary>
    public class MarketConfig
    {
        [JsonInclude]
        public Dictionary<string, string> Sources { get; set; } = new()
        {
            { "KitX Official Market Source", "https://cget.catrol.cn/KitX/v1/index.json" }
        };
    }
}

//
//                  ,:
//                ,?}'
//              =!J |
//            ,*-. ?&amp;
//            WP) Y9P
//           YP   ,W'
//          ,W)  ,WW.'
//          WW) ,WWW)
//          7W),WWWW'
//          `WWWWWW'
//           9---W)
//       ,,--WPL=YXW===
//      (P),CY:,I/X'F9P
//      WUT===---/===9)
//      -HP+----Y(C=9W)
//       '9Y3'-'-OWPT-
//        'WWLUIECW
//         (:7L7C7'
//        ,P--=YWFL
//        Y-=:9)UW:L
//        3-'9=WU/.7
//       ,WP9HTFUW'()
//        9W7W))UF 9)
//        7WYW))PW W
//         7WH)),WC)
//          7L--/XY)
//          9+-,KY7)
//          W9-Y3+7)
//          W'=9WI7)
//         ,W  '-YY)
//          W    ::W                ,
//         ,T     :X)              ()
//         ()     '9W  'L.         ()         ,-
//         (C     =:9   '9L        ()        ,T
//         ()    ,,-7)    7WL      WW      ,F'
//         ()    , T9)     '9WL    --    ,YF
//         ()    '-/(W       -==+PE9P7===O)          -,
//         'W, ,  T+/WX=L-. ,WP+()+3L3,),=WL  --==-T-
//          7)    -,YW '-=9WPL+PT-- ':--L/=9WP=-'
//          'W-,.-,++W.   WWHP    ,,-/  .9CP3)
//           W  --':-9:7=9W-T ,-=FT''=++,(TFYW=====---,
//           W    .-='/.  7W-,WE=--,,=-:9H=9W""~~~~~~'
//           ()   ':'/Y,  (L-9PXWWW,YWWX,(U3C        
//           9' ,,::/Y,/,  7LW+'-'7)()-'(MWW)
//        ,,-/:',T,'-:',) ,3WWW, .Y=W'.(+WPW)
//       ,F=T:9/:':C' /W),WMW9PO),m-+--9+WYW)
//      ,3Y:/--.'-,',F=FHWWE/LMWU.'--X3CWW(WL
//      YP:/:' -/'-Y-,W-T)9X,WCWWWX=WWWW39/OW
//      7WF:=,/:-:P:,P(-'))PWWHYT79WWWHPW0W7W'
//      'WU7C-:=-=-C9'WF,):):H7L   '7CI7WEXP'
//       7L-,Y==3F:::,=,:-/,'P=.,  ':79UWEW)
//       'WEW9P=/,)/ -:,P: / L7:'-=,-+YMWWW)
//        'W)+=T,T()/-,F,,,),)  ',.-+(L=W9WW.
//         '+C/:I'''',P:''/ '  ''9.  == '-'7-
//          (W-+'. ,YF )/:'      ')-. ,-:FX-L
//          'WM/',/CP /,:'    ..:)  ,T','/: 'W,
//           W--,YXT /'')   ,P=-/',P'  '(:'  'W,
//           (WEXWF Y' ,)  ,/'-,,YT    ///  ,,'W.
//          ,WWWWT,,' .Y:/.',,-,=',- ,YY(). +3,W)
//          WFXF:,'P ,,)/  ,',P',,- ,FI,))) I3'W)
//          -HP,X'',/ '  ,/,/' ,/',,P3'I(:) W) W)       /=+=,
//           9WY).,/'  ,/'-'   ,-=9-/'Y'((',W) PW      /'  '-==L,
//           'WY,'    ,/,P   ,YP- C/',',)( (W'(WW.    /'       '7==L.
//            ()'    /:/' ,,WT'  3F',' /)W (W (K()   /'   .        '7X
//            ()   ,P,P',)T=:- ,WP'.' ,P,T (W (-9L ,Y)' ,X//, .    Y:P
//           ,F   ,F,',--,/:' ,+P' '  Y):) (E' YHWLWT)-''-9/',-' ,,,WF
//          ,P.,P,)-3-- ,-,' ,WF.    ,Y (' (L-WCTWEW30V-/',:'=/P+E7WF
//          W- Y,P/C)',Y',' ,WT      Y) :  (P-=Y:UW9CX)3-=- ,W:9/PXXW.
//         /T./:P/)' ,P',' YW-      ,P'',  9M).())WTHW3,C'  9C9='W3WW)
//        ,EPOP/YR. /F ,',/W)       /'  :  (W)'W979WO0=WC:,..9LPXWWP-
//        3H:WL-R' /' /' /WF       ,) ,,   (U'(HW=WWXO:--:,:'(W=WWF'
//       ,WLWWWI:,F' /-'3WF '      Y  ) ,  (),T(0)WO9YPL.' ',WP=='
//         --YWX-F  Y',WWT' :':   (' ()7)  (MT: WP)3C)-''  3C'
//             WF  /' YW--,  ,    Y  W (),YM+C' 9+I3UV:' .YP'
//            (T  3',H3-.. ,..  .,) ,) ()F-=T-. (0,9L,'  /P'
//            ,W Y' 33P  .  /    Y  Y) (Y' R,:  7)Y+-),,=W'
//            /',F.,W)     ,,.' ,) ,W) +)  3),  (WT9XW=3P'
//           /F:T.:WF.  '..:'   :' (W. 7) '=),  'WT7WWP '
//          ,P,F''WF  . , :-': ,)  YC../) 'HY.   WP0WC'
//         ,P:9::YP   '  '('   :   W) .W)  +3)   9TLWC
//         (P/Y(,P' ... '':, .,)  ,W) :3)  X+.   WFUW)
//         'WW),I','  .., =  ':  ,O+' ,W'  )9,   99U()
//          7W,='.,' ' :.'. . '  ,W)  =3   )+.  ,OH:O)
//          'L,F,: '. :C::' '    (W)  9W   7+    'H,:L
//           7W'++: .. ,':' '    YT   Y).  :-.    XU:W
//           (T':,''','','       3'  ,-)   ,-'    77XW
//           (W),J.-:/-:))'      P   )9)   :,Y  .  T,9)
//           (WUI:TY:,,,:,      /' ,- W)   YC:     9/7)
//           (U),-:-''.'=      (:,F' (W)   ,Y.     3=:L
//            (),:::',)/'    ,,F9W'  YW)   /L.    .7=9W,
//            (LUL-L.T-'.' ,WXM(W)   3W)   'U.     ,)-W
//            3X=((:,' ' ,WMWF-(+'   WW'   '=,'    ,ICW
//           ,T)=)K-=':-WPIWP':,:   ,WW    +/, ..' :+,9)
//           Y):LX:.:=EHR,PU:'/''   (WW    I:=, .,-9CO)
//           ()-+,,HPT+C:W9= ,)'    /WT'   T.: --PCXCKF'
//           7LIHTP+OY3LW'3:,L..    WW)    ,,(W('MX'WT'
//            7T,I-:XF:WF(: ,)    ':WWT  ,=PT:T(AY) W
//            (PWW)W3=/P,P  ,     ,'WW),YP,WH,)Y)TWX9)
//            3)OWRE)-YUY'... '  ..(WWXWW)9W+C)WUP9P3'
//           ,WTHEF:LOP:W ' ,.   ,:(WPY(W,(P::)W(P3+)
//           (P3WF/:WM:() :.:      (WHY)39HC'U()(W,W)
//           (LW9/CWY-,E'  ,'     'YPL/T:WP:,(()3Y W)
//           (TLUEEP=7W+.,:)       -P-:,PWT.:Y()() W+
//           (EP/30-OAT .'3  . . . .C,P):WP Y)()3C:PW
//           (PWMH:FXW'',-(, '   ',97WMU(7: )LW W .WW
//           (WOWF-7EP)-X3., , ,,WP+WYY+YW' )WW 3 .W7)
//            9W93UOY):.)/.- :YWCWU-EIMC)E (-WP + ,WW'
//            (XWYUWY.,:'.,,YE3-7WE3WXV(UT,( W),T =P'
//            (PT709),)C:/FY9)T.(W9YHL/Y(C T,W)') W.
//            (+UTYH-:-=C-(P(-).WWF3:))3(U))(W)() P'
//            3P7Y3)/'XP:)WP(J. WXCWKV:)()))(W'U)()
//            7OLY3',H9),YW'F ),W)CT)/Y((-))'W,U)()
//            7F=T-/T(=)A3C,)3)(WA()=)TY(CY'YWY(::)
//            W9C=()L/3,9'/('Y,YWU(XE/))()E.YT)3:)L
//            W=P:F:(,)),,'F'/:WP+3OY':)(R+ /T,T')W
//         -=WRHX9C9-W'=,),)'A,A)XW779EXWK+.()3W),(,
//       ,W=-'L,,XX)/)+'I 3)39I(UHE-+LX39TWH/LUP)(H)
//      ,P:. ,-90/,(F0'/:,W //'(YOC':--YY3/IRW'9LT')
//      3W=:Y:-F.)Y:/''Y /O.=:,WL9) ,. ,.79=9PL'9(-W,
//      WWWWWWWPT:,::/'-WH=9',P=-W3XU3-,W=YL-O3-O)X9WL
//       -7T--''=9W==W=9WWHW====''P======='---=T==F==9)
// 
//
