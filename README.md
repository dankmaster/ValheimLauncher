Valheim Mod Launcher
A simple mod management tool that helps you distribute mods to your friends for your Valheim server. Perfect for server admins who want to make mod management easier for their players.
🚀 Key Features

Auto-Updates - Launcher keeps itself and mods up to date
Easy Install - Players just run the exe
Simple Updates - Admins just update one zip file
Steam Integration - Automatically finds Valheim
Safe Updates - Verifies all mod files

📥 For Players

Download ValheimLauncher.zip from [Releases]
Extract and run ValheimLauncher.exe
Click "Yes" when updates are available
Play!


Note: You need BepInEx installed in your Valheim directory first

🛠️ For Server Admins
Want to use this for your own server? It's easy!
Initial Setup

Fork this repository
Go to Actions tab in your fork
Enable GitHub Actions
Create your first release (see below)

Managing Mods

Prepare your mods:

Collect your BepInEx plugins
Create a zip named plugins.zip
Structure should match your BepInEx plugins folder


Update the mods:

Upload plugins.zip to the Mods folder in your repo
Commit and push
Players will get the updates automatically



Creating Releases

Go to Actions tab
Click "Build and Release"
Click "Run workflow"
Choose version type:

patch for small updates (1.0.0 → 1.0.1)
minor for new features (1.0.0 → 1.1.0)
major for big changes (1.0.0 → 2.0.0)



The workflow automatically:

Builds the launcher
Creates a release
Updates version numbers
Uploads everything needed

Distribution

Share the launcher with your players
Update mods by replacing plugins.zip
Players get updates automatically

📋 Requirements
For Players

Windows 10/11
Valheim (Steam version)
BepInEx installed

For Server Admins

GitHub account
Your server's mod files

❓ Common Issues
Q: Launcher can't find Valheim
A: Run as administrator
Q: Mods not working
A: Make sure BepInEx is installed
Q: Updates fail
A: Run as administrator, check antivirus
🆘 Support
Having problems?

Run as administrator
Check BepInEx installation
Check Issues
Create new issue with your launcher log


Made for Valheim server admins who just want to help their friends keep mods updated!
