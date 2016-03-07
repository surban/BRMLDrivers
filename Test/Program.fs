open BRML.Drivers

let cfg : XYTableConfigT = {
        PortName       = "COM7";
        PortBaud       = 115200;
        X              = { StepperConfig = {Id=1; AnglePerStep=1.8; StepMode=8;}
                           DegPerMM      = 1.8;
                           Home          = Stepper.Right;
                           MaxPos        = 145.; }
        Y              = { StepperConfig = {Id=2; AnglePerStep=1.8; StepMode=8;}
                           DegPerMM      = 1.8;
                           Home          = Stepper.Left;
                           MaxPos        = 140.; }
        DefaultVel     = 30.;
        DefaultAccel   = 300.;
        HomeVel        = 50.;    
}

[<EntryPoint>]
let main argv = 
    use xyTable = new XYTableT(cfg)

    xyTable.Home() |> Async.RunSynchronously

    0 
