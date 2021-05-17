# Mesen-S Ozi's hack

Mesen-S is a cross-platform SNES emulator for Windows & Linux built in C++ and C#.  
If you want to support this project, please consider making a donation:

This version is a hacked verion by Oziphanto(m) which adds some 64Tass support and new fetures to aid SNES Development. See Below

[![Donate](https://www.mesen.ca/images/donate.png)](https://www.mesen.ca/Donate.php)

## Development Builds

Development builds of the latest commit are available from Appveyor. For release builds, see the **Releases** tab on GitHub.

**Warning:** These are development builds and may be ***unstable***. Using them may also increase the chances of your settings being corrupted, or having issues when upgrading to the next official release. Additionally, these builds are currently not optimized via PGO and will typically run a bit slower than the official release builds.

Windows: [![Build status](https://ci.appveyor.com/api/projects/status/cjk97u1yvwnae83x/branch/master?svg=true)](https://ci.appveyor.com/project/Sour/mesen-s/build/artifacts)

Linux: [![Build status](https://ci.appveyor.com/api/projects/status/arkaatgy94f23ll3/branch/master?svg=true)](https://ci.appveyor.com/project/Sour/mesen-s-hayo4/build/artifacts)

## Releases

### Windows / Ubuntu

The latest version is available from the [releases tab on GitHub](https://github.com/SourMesen/Mesen-S/releases).  

### Arch Linux

Packages are available here: <https://aur.archlinux.org/packages/mesen-s-git/>

## Compiling

See [COMPILING.md](COMPILING.md)

## License

Mesen is available under the GPL V3 license.  Full text here: <http://www.gnu.org/licenses/gpl-3.0.en.html>

Copyright (C) 2019 M. Bibaud

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.


# Ozi's hacks
The main this I've added is 64Tass labels and Breakpoint/watchpoint/assert support. 
To generate a tass file in the right format you need to assemble with --dump-label -l [rom name].tass the order is important.

The system will detect and autoload a tass file for you, as long as you have the auto load option enabled ( it is by default ).

## BREAK Points
This is done by having a label have BREAK in it, anywhere in it. for example
~~~
RESET
    CLC
    XCE
    LDA #$01
    STA $420D
    JML RESETHi
RESETHi
_BREAK
~~~
This will add a breakpoint on RESETHi, sometimes you will want multiple breakpoints in a function/scope so you can just do
~~~
_BREAK
...code...
_BREAK2
~~~
etc. 
Be warned that anything with BREAK will trigger this so if you have
~~~
doOUTBREAK
...code...
~~~
this will flag a breakpoint.
~~~
doOutBreak
...code...
~~~
will not. 

## Asserts
Welcome to "the best"(tm) feature of a SNES emulator ever. You will be shocked to discover it took till 2021 for this to happen. 
~~~
_ASSERT_[condition]
~~~
It must be `_ASSERT` and the condition's are a sub set of what MESEN_S support with a custom encoding becaues label limits. This is a hack and a "mostly does most of the job most of the time" case not a "complete solves everything in the universe" solution. 
I've added a few short cuts for you. 
id | what it does|what it looks like in the debugger
---|-------------|----
_a8 | this will make sure at this point in the code A is 8 bits in size|(PS & 32) == 32
_a16 | this will amke sure at this point in the code A is 16 bits in size|(PS & 32) == 0
_xy8 | this will make sure at this point in the code X and Y are 8 bits in size|(PS & 16) == 16
_xy16 | this will make sure at this point in the code X and Y are 16 bits in size|(PS & 16) == 0
_axy8 | this will make sure at this point in the code A, X and Y are 8 bits|(PS & 48) == 48
_axy16 | this will make sure at this point in the code A, X and Y are 16 bits|(PS & 48) == 0
_jsl | this will *mostly* detect that the last jump with return was a long |jslf == 1
_jsr | this will *mostly* detect that the last jump was return was abs |jslf == 0

For example, you have a function like so
~~~
myButeFunc_aXY
	LDA #$04
	STA $2100
	RTS
~~~~
And you assume, beleive and hope that when anything calls this A will be 8 bits in size. But we all know it goes wrong, it happens. So now you do
~~~
myButeFunc_aXY
_ASSERT_a8
	LDA #$04
	STA $2100
	RTS
~~~~
Now if the CPU hits that code, and Status.M is 0 the assert will fire and tell you, No its not, you stuffed up, look at the call stack, work out where you came from and fix it. 

It gets better, because you can do conditions as well. 
What you have to type because labels | what it actually does
-------------------------------------|----------------------
_0x | $ ( yeah I hate it too but all I could think, you got a better one hit me up)
EQ | ==
LT | <
LTE | <=
GT | >
GTE | >=
NE | !=
AND | &&
OR | \|\|
LBRAC | (
RBRAX | )

Lets imagine you have an array, that is index by X, it would be a same if the code got to it with an invalid X that is too far right?
~~~
LDA #0
_ASSERT_X_LT_16
STA label,X
~~~
Now if something calls this code or function and X is not what you think it is, the assert will fire. 
I also added 2 new registers to the mix, don't know why there were missing but they were.
`DBR` and `DP` this means you can `_ASSERT_DBR_NE_0x7e_AND_DBR_NE_0x7f` to make sure that when you are using your code and writing to registers, you are not actually looking at the WRAM. And DP is the direct page, so if you have some code that needs it to be zero `_ASSERT_DP_EQ_0` or if you have a function to assumes it has been moved to somewhere else then `_ASSERT_DP_EQ_0x400` if it should be $400.

Please note all sub commands are "to lowered" before checking, so the _ASSERT must be upper case everything else can be upper, lower or mixed as you please. ie. `_ASSERT_X_LT_16` = `_ASSERT_x_lt_16` = `_ASSERT_X_Lt_16` etc

### JSL/JSR
The `ASSERT_JSL` and `ASSERT_JSR` cases, a horrible hack, that will probably work 99.98% of the time. What it does:

It sets a flag internally when it does a JSL and clears it when it does a JSR. Thus when you get to the destination you check if this flag was what you wanted. 

The flaws:
- well IRQs/NMIs if they fire during the JSR/JSL read, then the interrupt will trash the state. I handle this of cause. What it does is on an IRQ it shift the internal flag to the left, and then back to the right on an RTI. Thus IRQ NMI etc all have their own flag.
- The register is only 16 bits, so you only get 15 levels of IRQ/NMI nesting before data is lost.
- It assumes a 1:1 relation ship between Interrupt and RTI. So if you do any fancy super stable double IRQ setups this will break this feature. 
- this 1:1 is also broken if you fire an RTI as some other optimistation, or your codes goes walkies and fires 3 or 4 haphazzadly. 

Usage
~~~ 
lMyFunc
_ASSERT_JSL
..code...
RTL 

MyOtherFunc
_ASSERT_JSR
...code...
RTS
~~~


## WATCH
This is used to add read/write/redwrite breakpoints of a size to a variable. for example
~~~
MyVar .byte
_WATCH_READ_BYTE
~~~
it must start `_WATCH` and your sub options are, in any order you prefer
option | meaning
-------|--------
LOAD   | break upon read/load
READ   | break upon read/load
STORE | break upon write/store
WRITE | break upon write/store
READWRITE | break upon write/store or read/load
WRITEREAD | break upon write/store or read/load
LOADSTORE | break upon write/store or read/load
STORELOAD | break upon write/store or read/load
BYTE | does nothing but makes you feel better
WORD | sets the range for 2 bytes worth
LONG | sets the range for 3 bytes worth

Note the address it covers is -1 -> -3 from the label, as the label is **after** the label so in the file will have the address post the actuall fields address. This in the code I do -1/-2/-3 to get the start. This if you have a _long_ and do `_WATCH_READ_WORD` this will break on the upper word of the long not the lower word of the long. 

And again please not that the `_WATCH` must be upper case everything else is "to lowered" and hence you can just do `_WATCH_load_long` if you want. 

## 64Tass is not what I use
Well not everybody is perfect and it maybe to late for your current project, I understand. Good news is tass format in how I use it is simple enough that you can easily fake it. 
Here is an except from a real one.
```master.asm:112:1: Bank80 = $808000
Bank80.asm:170:1: Bank80.mBG2SC = $800011
Bank80.asm:262:1: Bank80.SpriteEmptyVal = $808163
Bank80.asm:151:1: Bank80.MainLoop = $808104
Bank80.asm:202:1: Bank80.DMAZero = $80810f
Bank80.asm:185:1: Bank80.mWOBJSEL = $80002f
Bank80.asm:180:1: Bank80.mBG3VOFS = $800025
master.asm:130:9: Bank80.mBG3VOFS.lo = $800025
```
Now the MESEN-S import doesn't give two hoots about the path at the start, it just skips it, what does matter is the ' ' after it. 
Thus `dummy [label]=[address]` is all you need to do. addres can be in hex with `$` or decimal. 