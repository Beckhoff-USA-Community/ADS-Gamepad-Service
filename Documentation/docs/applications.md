# Applications

A gamepad in a TwinCAT project is process data like any other, so what you do with it is limited by the application, not by the service. This page collects patterns that have proven themselves and the safety thinking that should come with them.

## Where a gamepad earns its place

* Jogging servo axes during commissioning, with the stick as a velocity setpoint. The response curve of the TcCOM module, or your own shaping in PLC code, gives fine control near the center and speed at full deflection.
* Driving XPlanar movers or XTS shuttles by hand: two sticks map naturally onto a plane, and a mover following a stick is the fastest way to get a feel for a new station layout.
* Positioning robot kinematics such as ATRO during setup and teaching.
* Animatronics and show control, where an operator performs a motion live and the program records or repeats it.
* Any machine function that a technician wants in hand during service: conveyor inching, camera or light positioning, test rigs.

In every case the pattern is the same: the gamepad is an input device, the application decides what the values mean. The library and the module both hand you clean values in fixed ranges, so the mapping to axis commands stays a few lines of your own code, visible and reviewable.

## Safety thinking for hand held control

A gamepad is a commissioning and operator tool, not a safety device. Nothing here replaces a machine's safety functions, an enabling switch or a risk assessment; treat the gamepad as one more standard input the safety design has to account for.

That said, the service and its consumers are built so the failure directions are predictable, and a program should use them:

* **Watch the connected state every cycle.** When a pad disconnects, whatever the reason, every input reads zero and the connected bit clears in the same cycle. Sleep, a drained battery, a pulled cable and a radio drop all arrive the same way. The program decides what the machine does next: hold position, alarm, or hand control elsewhere.
* **Use an enable button as a deadman.** Gate motion on a held shoulder button so that letting go of the pad stops motion by design, not just by the sticks springing back to zero. This also covers the rare case of an input stream that freezes while the pad still reports as connected.
* **Watch the report counter on wireless pads.** Programs reading the extended block get a report counter, P_Sequence in the library and nSequence in the module, that moves with every report from the pad. A counter that stops while the pad claims to be connected means the stream is stuck, and a small watchdog on it catches what the connected bit cannot.
* **Plan the reaction to a drop, then test it.** Pull the cable or power the pad off mid motion once, on purpose, during commissioning. The inputs zero and your program's reaction runs; better to see it then than in production.

## Update rate and latency

The service has no cycle of its own: every ADS read polls the pad's latest state, so the effective update rate is the PLC or task cycle time, and the data is as fresh as the request. The pad itself reports many times faster than a typical PLC cycle, over USB and over Bluetooth alike, so the cycle time stays the dominant term. For jogging and hand control this is comfortably below what an operator can perceive; there is no reason to build extra buffering on top.
