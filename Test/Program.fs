open BRML.Drivers

open System.Threading

let cfg : XYTableConfigT = {
        PortName       = "COM7";
        PortBaud       = 115200;
        X              = { StepperConfig = {Id=1; AnglePerStep=1.8; StepMode=8; StartVel=1000.;}
                           DegPerMM      = 360. / 1.25;
                           Home          = Stepper.Right;
                           MaxPos        = 147.;}
        Y              = { StepperConfig = {Id=2; AnglePerStep=1.8; StepMode=8; StartVel=1000.;}
                           DegPerMM      = 360. / 1.25;
                           Home          = Stepper.Left;
                           MaxPos        = 140.;}
        DefaultVel     = 30.;
        DefaultAccel   = 30.;
        HomeVel        = 10.;    
}

[<EntryPoint>]
let main argv = 
    use xyTable = new XYTableT(cfg)

    printf "Homing..."
    xyTable.Home() |> Async.RunSynchronously
    printfn "Done."

    printfn "Drive to center"
    xyTable.DriveTo((70., 70.)) |> Async.RunSynchronously

    printfn "Drive with constant velocity"
    xyTable.DriveWithVel ((10., 10.)) 

    Thread.Sleep 15000

    printfn "Exiting."
    0 
