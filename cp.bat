set	gamedir="H:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed"
set	moddir="C:\Users\xxxx\Documents\Klei\OxygenNotIncluded\mods\Dev\Horizon"

#copy	%gamedir%\Assembly-CSharp.dll				%cd%
#copy	%gamedir%\Assembly-CSharp-firstpass.dll		%cd%
#copy	%gamedir%\0Harmony.dll						%cd%
#copy	%gamedir%\UnityEngine.dll					%cd%
#copy	%gamedir%\UnityEngine.CoreModule.dll		%cd%
#copy	%gamedir%\System.dll						%cd%

del	/s	/q	%moddir%\*.*
xcopy	/s	%cd%\000lib\*.*							%moddir%
copy	%cd%\Horizon\bin\Debug\Horizon.dll			%moddir%