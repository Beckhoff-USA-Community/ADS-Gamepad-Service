# Versions and Wire Contract

The wire format between the service and its consumers is treated as frozen. Additions are made in reserved space and announced through a version handshake, so existing programs keep working or fail loudly, never silently.

## Version matrix

| Service | Wire contract | Notable |
| --- | --- | --- |
| 2.0.0 | 1.0 | First release under the new name; .NET 10; true Windows service; TwinCAT packages |
| 2.1.0 | 1.1 | Service info block and version handshake; install location moved to the product directory |
| 2.2.0 | 1.2 | PlayStation DualSense backend on Windows; Create and Options on the reserved button bits |
| 2.3.0 | 1.2 | Beckhoff RT Linux support |

Library: AdsGamepad 2.0.0 replaces XboxControllerUtilities, whose final release is 1.5. TcCOM module versions are independent; the module reads the same contract. Any 2.x service serves any consumer, since the controller block never changed shape.

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
