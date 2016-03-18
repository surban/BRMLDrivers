open BRML.Drivers

open System
open System.Threading


let tblCfg : XYTableConfigT = {
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

let linmotCfg: LinmotCfgT = {
        PortName       = "COM4";
        PortBaud       = 57600;
        Id             = 0x11;    
        DefaultVel     = 50.0;
        DefaultAccel   = 200.0;    
}

let demoTable (xyTable: XYTableT) = async {
    printf "Homing XYTable..."
    do! xyTable.Home() 
    printfn "Done."

    printfn "Drive to center"
    do! xyTable.DriveTo((70., 70.)) 

    printfn "Drive with constant velocity"
    xyTable.DriveWithVel ((10., 10.)) 

    do! Async.Sleep 2000

    printfn "Stop."
    xyTable.Stop ()


    let rng = Random()

    for i = 1 to 20 do
        let xpos = rng.NextDouble() * tblCfg.X.MaxPos
        let ypos = rng.NextDouble() * tblCfg.Y.MaxPos
        let pos = xpos, ypos
        printfn "Drive to %A" pos
        do! xyTable.DriveTo(pos)

    //printfn "Exception..."
    //failwith "my error"
}


let demoLinmot (linmot: LinmotT) = async {
    let rng = Random()
    for i = 1 to 150 do
        let pos = rng.NextDouble() * (-20.)
        printfn "Drive to %f" pos
        do! linmot.DriveTo (pos)

    //printfn "Power off"
    //do! linmot.Power false 
}

let demoBiotac (biotac: BiotacT) =
    let nSamples = 1000

    printf "Waiting for biotac initialization..."
    biotac.GetNextSample () |> ignore
    printfn "Done."
    printfn "Biotac serial: %s" biotac.Serial.Value

    let sw = System.Diagnostics.Stopwatch.StartNew()
    for i = 1 to nSamples do
        biotac.GetNextSample() |> ignore
    let duration = sw.ElapsedMilliseconds
    let freq = (float nSamples) / ((float duration) / 1000.)
    printfn "Biotac sampling rate: %f Hz" freq
    printfn ""

    let sx, sy = Console.CursorLeft, Console.CursorTop
    while not Console.KeyAvailable do
        Console.CursorLeft <- sx
        Console.CursorTop <- sy

        let smpl = biotac.GetNextSample()
        for chnl in smpl.Flat do
            printf "%04x " chnl  
        printfn ""


let doDemo linmot xyTable = async {
    let! dl = demoLinmot linmot |> Async.StartChild
    let! dt = demoTable xyTable |> Async.StartChild

    let! dtRes = dt
    let! dlRec = dl
    ()
}


[<EntryPoint>]
let main argv = 
    //use linmot = new LinmotT(linmotCfg)
    //use xyTable = new XYTableT(tblCfg)
    use biotac = new BiotacT(uint32 1364033083, 0)

    printf "Homing Linmot..."
    //linmot.Home(false) |> Async.RunSynchronously
    //linmot.DriveTo(-1.) |> Async.RunSynchronously
    printfn "Done."    

    //doDemo linmot xyTable |> Async.RunSynchronously

    demoBiotac biotac

    printfn "All done."
    0 
