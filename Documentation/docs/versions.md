# Versions and Wire Contract

The wire format between the service and its consumers is treated as frozen. Additions are made in reserved space and announced through a version handshake, so existing programs keep working or fail loudly, never silently.

## Version matrix

| Service | Wire contract | Notable |
| --- | --- | --- |
| 2.0.0 | 1.0 | First release under the new name; .NET 10; true Windows service; TwinCAT packages |
| 2.1.0 | 1.1 | Service info block and version handshake; install location moved to the product directory |
| 2.2.0 | 1.2 | PlayStation DualSense backend on Windows; Create and Options on the reserved button bits |
| 2.3.0 | 1.2 | Beckhoff RT Linux support |
| 2.4.0 | 1.2 | Rewritten documentation site; the service itself is unchanged |
| 2.5.0 | 1.3 | Extended controller data block; read it with library 2.1.0 or newer |
| 2.6.0 | 1.4 | Battery detail in the extended block, verified against a full charge cycle; read it with library 2.2.1 |
| 2.7.0 | 1.4 | DualSense over Bluetooth on Windows; the wire contract is unchanged |

Library: AdsGamepad 2.0.0 replaces XboxControllerUtilities, whose final release is 1.5. AdsGamepad 2.1.0 adds the extended block support and works against any 2.x service; against a service older than 2.5.0 the extended data simply reads zero. AdsGamepad 2.2.1 adds P_Ext_Battery for the battery fields a 2.6.0 or newer service fills. TcCOM module versions are independent; the module reads the same contract. Any 2.x service serves any consumer, since the controller block never changed shape.

## The controller block

An ADS read is addressed by two numbers, the IndexGroup and the IndexOffset. Each controller slot answers a read of 32 bytes at IndexGroup controller number times 16#10000, IndexOffset 0. A disconnected controller returns all zeroes with success; consumers branch on the connected bit, not on the error code.

| Offset | Type | Content |
| --- | --- | --- |
| 0 | DINT | Controller number, 1 to 4, zero while disconnected |
| 4 | REAL | Left stick horizontal, -100 to 100, right positive |
| 8 | REAL | Left stick vertical, -100 to 100, up positive |
| 12 | REAL | Right stick horizontal |
| 16 | REAL | Right stick vertical |
| 20 | REAL | Left trigger, 0 to 100 |
| 24 | REAL | Right trigger, 0 to 100 |
| 28 | WORD | Buttons, bit table below |
| 30 | WORD | States, bit table below |

Buttons: bit 0 DPad up, 1 DPad down, 2 DPad left, 3 DPad right, 4 Start, 5 Back, 6 left stick click, 7 right stick click, 8 left shoulder, 9 right shoulder, 12 A, 13 B, 14 X, 15 Y. From contract 1.2 a PlayStation pad additionally sets bit 10 for Create and bit 11 for Options, on top of their mapping onto Back and Start; Xbox pads never set those bits.

States: bit 0 connected; bits 1 to 5 battery type as disconnected, wired, alkaline, NiMH, unknown; bits 6 to 9 battery level as empty, low, medium, full.

Rumble: an ADS write of 8 bytes to the same IndexGroup at IndexOffset 16, two REAL values 0 to 100 for the left and right motor. The PLC library clamps before sending.

## The service info block

Since contract 1.1 the service answers a 32 byte read at IndexGroup 16#F000: contract major and minor, service major, minor and patch as words, a reserved word, then a capability double word with bit 0 for the XInput backend and bit 1 for the DualSense backend. The remaining bytes are zero.

The PLC library reads this block once at startup and reports the result in P_Handshake_State: Compatible when the contract major matches, Unsupported against a service older than 2.1.0, which does not serve the block, and Mismatch when the service speaks a newer contract major than the library knows. Data exchange keeps running in every case; the handshake informs, it never blocks.

## The extended block

Since contract 1.3 each controller slot additionally answers a read of 96 bytes at IndexGroup controller number times 16#10000 plus 16#100, IndexOffset 0. The block carries the data a DualSense offers beyond a classic gamepad. A slot that cannot supply it, for example an Xbox controller or a disconnected pad, answers all zeroes with success; consumers branch on the flag bit, never on sensor noise. A service older than 2.5.0 answers the read with an error, and the info block capability bit 2 states whether the block is served.

| Offset | Type | Content |
| --- | --- | --- |
| 0 | UINT | Size of the block, 96 while served |
| 2 | UINT | Layout version of the fields below, 1 today |
| 4 | WORD | Extended buttons: bit 0 PS, bit 1 Mute, bit 2 touchpad click |
| 6 | WORD | Flags: bit 0 set while this slot supplies extended data |
| 8 | INT, three times | Gyro X, Y, Z in raw sensor units |
| 14 | INT, three times | Accelerometer X, Y, Z in raw sensor units |
| 20 | 6 bytes | First touch contact: active, contact number, X 0 to 1919, Y 0 to 1079 |
| 26 | 6 bytes | Second touch contact, same shape |
| 32 | UDINT | Report counter from the pad, widened by the service. A value that keeps moving proves the input stream is alive |
| 36 | USINT | Battery charge, 0 to 100 percent, since contract 1.4 |
| 37 | USINT | Battery flags: bit 0 charging, bit 1 full, since contract 1.4 |
| 38 | 2 bytes | Reserved, zero |
| 40 | 56 bytes | Reserved, zero |

The motion values are raw on purpose: interpreting or filtering them is application code, like everything else in this project. The Create and Options buttons are not repeated here; they stay on bits 10 and 11 of the classic button word.

Since contract 1.4 the flag word additionally carries bit 1 while the battery fields hold data. The battery encoding was verified against a real pad through a complete charge cycle. When the pad reports a state the service cannot interpret, for example a temperature fault, the battery fields read zero with the flag clear rather than guessing. The classic battery bits in the States word of the controller block are unchanged; a wired PlayStation pad keeps reporting wired and full there, and the extended block is the place for the real numbers.

In the PLC library the block is read by calling ReadExtended() once per cycle in addition to Cycle(). It costs one extra ADS read per cycle, and programs that never call it behave exactly as before. The values arrive through P_Ext_Buttons, P_Touchpad, P_Motion, P_Sequence and, with library 2.2.1 against a 2.6.0 or newer service, P_Ext_Battery.
