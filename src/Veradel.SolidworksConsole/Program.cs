using SolidWorks.Interop.sldworks;
using System.Dynamic;
using System.Runtime.InteropServices;
using Veradel.SolidworksConsole.Boilmaker;
using Veradel.SolidworksConsole.PartCreation;
using Veradel.SolidworksConsole.BalloonLocation;

namespace Veradel.SolidworksConsole
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SldWorks swApp = GetSolidWorksApplication();

            /*
             
            // Default action: debug "Colorear aristas". Select the offending edge(s) in the drawing view
            // first, then run this. Pass "chomp" as an argument to run the old part-creation demo instead.
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "chomp", System.StringComparison.OrdinalIgnoreCase))
            {
                RunChompDemo(swApp);
                return;
            }

             */
            //new EdgeColoringDebug(swApp).Run();
            /*
             
            // "uniforme" => una sola escala para todo el despiece; por defecto, una por cuerpo
            DespieceScaleMode scaleMode =
                args != null && args.Length > 0 &&
                string.Equals(args[0], "uniforme", System.StringComparison.OrdinalIgnoreCase)
                    ? DespieceScaleMode.Uniform
                    : DespieceScaleMode.PerBody;

            new Boilermaker(swApp, scaleMode).Run();
             
             */

            BalloonLocation.BalloonLocation a = new BalloonLocation.BalloonLocation(swApp.ActiveDoc);
            a.Execute();


        }

        private static void RunChompDemo(SldWorks swApp)
        {
            #region BoltCreation
            /*
             BoltCreation create = new BoltCreation(swApp,30,5,20,30);

            create.SetGroove(22, 1, 18);
            create.SetChamfer(20, 1);
            create.CreateBolt();
             */
            #endregion

            ChompCreator chomp = new ChompCreator(swApp);
            chomp.SetBody(440, 70);

            Step[] steps = { new Step(34, 5.5), new Step(29, 8.5), new Step(32, 47.8), new Step(33, 1.7) };

            chomp.SetRollerHousing(steps, 0, 8, 40);
            chomp.SetBodyCut(18.5, 88.5);
            chomp.SetSecondBodyCut(5, 410);
            chomp.SetChamferBodyCut(9.9, 10.13, 9.84, 5.85);
            chomp.Create();
        }


        // Get Solidworks session
        public static SldWorks GetSolidWorksApplication()
        {
            return (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        }

    }


}
