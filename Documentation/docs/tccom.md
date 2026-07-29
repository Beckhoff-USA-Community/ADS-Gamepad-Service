# The TcCOM Module

The Gamepad TcCOM module is a TwinCAT C++ module that reads a controller from the service and exposes it as linkable process data, with no PLC code involved. You add an instance to a TwinCAT project, assign it to a task, set the controller number, and link the outputs like any other process data. It polls the service once per task cycle and zeroes all outputs when the service stops answering.

The module ships as source with the engineering workload, installed under C:\Program Files\Beckhoff USA Community\ADS Gamepad\TcCOM. On Windows and TwinCAT/BSD, TwinCAT loads a C++ module only when it is signed with a certificate the target trusts, and a finished binary from someone else cannot be signed after the fact. On Beckhoff RT Linux the build result loads without signing. In every case the module ships as source, so building it yourself is the path. Copy the project to a writable folder first; the readme inside covers the requirements, the build, and the signing options.

## Parameters

| Name | Default | Meaning |
| --- | --- | --- |
| ControllerNumber | 1 | Controller slot on the service, 1 to 4. One instance serves one controller. |
| ServiceAmsNetId | 127.0.0.1.1.1 | AMS Net ID of the machine running the service. The default resolves the local system automatically. |
| TimeoutCycles | 1000 | Task cycles without an ADS answer before the outputs are zeroed and the read is retried. |
| Stick and trigger tuning | | Deadzone percent and response curve per stick and trigger, as an init set stored in the project plus an online twin set for live tuning during commissioning. |

## Outputs

Decoded, ready to link: the connected state, one BOOL per button, shaped stick and trigger values, the raw wire words for power users, communication diagnostics including a data age counter, and the service version information from the handshake.

## Wiring checklist

Assign the task under Context, then check the Interface Pointer tab: CyclicCaller must hold the object id of that task. XAE normally fills this in when you set the context, and the module refuses to reach OP while it is 0, on purpose, so a half wired instance fails loudly instead of idling.
