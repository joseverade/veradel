using SolidWorks.Interop.sldworks;
using System.Dynamic;
using System.Runtime.InteropServices;
using Veradel.SolidworksConsole.PartCreation;

namespace Veradel.SolidworksConsole
{
    public class Program
    {
        public static void Main(string[] args)
        {

            SldWorks swApp = GetSolidWorksApplication();
            #region BoltCreation

            //BoltCreation create = new BoltCreation(swApp,30,5,20,30);
            //
            //create.SetGroove(22, 1, 18);
            //create.SetChamfer(20, 1);
            //create.CreateBolt();
            #endregion

            ChompCreator chomp = new ChompCreator(swApp);
            chomp.SetBody(400, 70);

            chomp.Create();
        }


        // Get Solidworks session
        public static SldWorks GetSolidWorksApplication()
        {
            return (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        }

    }


}
