using KitX.Contract.CSharp;
using KitX.Web.Rules;

namespace TestPlugin.WPF.Winform
{
    public class Controller : IController
    {
        private readonly MainWindow mainwin;

        public Controller(MainWindow mainwin)
        {
            this.mainwin = mainwin;
        }

        public void End()
        {
            mainwin.Close();
        }

        public void Pause()
        {
            mainwin.Hide();
        }

        public void Start()
        {
            mainwin.Show();
        }

        public Function GetFunctions() => new Function();

        public object Execute(string cmd, object arg = null)
        {
            return null;
        }
    }
}
