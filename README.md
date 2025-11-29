**Setup instructions**

1. Pull the project: `git clone https://github.com/infernoplus/JortPob.git`

2. Open the project in your IDE of choice. I use VS 2022 so that's what I'd recommend for simplicity.

3. Copy the file "settings.example.json" and rename it to "settings.json". This is an untracked file for personal settings.

4. Install ModEngine3 with the installer: https://www.nexusmods.com/eldenringnightreign/mods/213?tab=description

5. Make a new file called `elden-scrolls.me3` and in the same folder make a `mods` folder, open it in a text editor and use this profile.
```toml
profileVersion = 'v1'
[[supports]]
game = 'elden-ring'

[[packages]]
id = 'elden-scrolls'
source = 'mods\'

[[natives]]
path = 'mods\regbinmeme.dll'
```

Pic for reference:
<img width="1392" height="405" alt="image" src="https://github.com/user-attachments/assets/9dd04ba4-fc11-4346-b1fd-2eaecc792b15" />

6. Download this dll that skips regulation.bin file size checks at startup. This is only avaiable in the `?ServerName?` Discord, and the `Elden Scrolls Dev` Discord, for now. Without this the game fails to load. Add it to the `mods` folder you created above.
https://cdn.discordapp.com/attachments/1410970950895403040/1411525175802990794/regbinmeme.dll?ex=68c17f02&is=68c02d82&hm=ad33ccf3fda7c3acb4b20f15c630b47cdb3128f6649a64c419efd9ce9eb860b7&

7. Download the [AudioKinetic Launcher](https://www.audiokinetic.com/en/wwise/overview/) here. This will require to to create a (free) account.

8. Install the Wwise `Authoring` package for the `Microsoft > Windows` platform. Make note of the target directory.
<img width="1085" height="858" alt="image" src="https://github.com/user-attachments/assets/3e32afd3-b7a2-496f-83ea-715979af4871" />


9. Update the value of `WWISE_PATH` in `settings.json` to point at the location of the `bin` of the `Authoring` tools. It will look something like `C:\Audiokinetic\Wwise2024.1.8.8898\Authoring\x64\Release\bin\`.

10. Edit the settings file and set the paths to your game folders. Here is Nords for reference. Make sure the OUTPUT_PATH goes to the mods folder you just set up.
<img width="722" height="239" alt="image" src="https://github.com/user-attachments/assets/1b729b02-4936-489b-8049-0389114b9744" />


12. Download BSAUnpacker from nexus: https://www.nexusmods.com/skyrimspecialedition/mods/974

13. Run BSAUnpacker, load the Morrowind.BSA from data files and then hit extract all and tell it to ouput to the `Morrowind/Data Files` folder. If done correctly you will have a bunch files in `Morrowind/Data Files/Meshes` like this:
<img width="960" height="755" alt="image" src="https://github.com/user-attachments/assets/6fb8adce-1bf4-426d-914a-233331351aed" />

14. Install Blender: https://studio.blender.org/welcome/

15. Download and install the morrowind blender plugin: https://github.com/Greatness7/io_scene_mw
<img width="1157" height="701" alt="image" src="https://github.com/user-attachments/assets/b9fdd83d-a29f-40d6-bcfc-3af0190f625f" />

16. Download Nord's program NIF2FBX: https://github.com/Nordgaren/NIF2FBX

17. This is a commandline app that converts all nif files in a directory to fbx. Run the program with the parameters: `NIF2FBX.exe "P:\ath\to\blender.exe" "P:\ath\to\morrowwind"`
<img width="2754" height="913" alt="image" src="https://github.com/user-attachments/assets/592451c3-cb7b-4af1-8636-fcb2ae0de5c1" />

18. Download Greatness7's program Tes3Conv. **NOTE:** Needs to be version 0.4.0 currently. Newest version not supported yet. https://github.com/Greatness7/tes3conv

19. This is another command line tool so run it with parameters: `tes3conv.exe "Data Files\Morrowind.esm" "Data Files\Morrowind.json"`. You should now have a Morrowind.json in the same folder as Morrowind.esm.
<img width="961" height="612" alt="image" src="https://github.com/user-attachments/assets/8343e568-d7ed-4967-8a53-b394dce38e01" />

20. Download UXM and unpack the script folder for Elden Ring. This needs to exist in order to recompile ESD files. https://github.com/Nordgaren/UXM-Selective-Unpack
<img width="539" height="634" alt="image" src="https://github.com/user-attachments/assets/ac43e504-4b41-48a7-8f77-8b60c9a189ca" />

21. Run the project in your IDE. It will take a while to build.

22. Double click the `elden-scrolls.me3` file to run the game. Go to the church of elleh and you will be transports to Morrowwind.

You can use [Elden Ring Debug Tool](https://github.com/Nordgaren/Elden-Ring-Debug-Tool) or the [TGA Table](https://github.com/The-Grand-Archives/Elden-Ring-CT-TGA) to warp to the Church of Elleh and spawn items in. The latter requires Cheat Engine.
