<div align="center">
  <h1>Valheim Mod Launcher</h1>
</div>

<p>A simple mod management tool that helps you distribute mods to your friends for your Valheim server. Perfect for server admins who want to make mod management easier for their players.</p>

<h2>🚀 Key Features</h2>
<ul>
  <li><strong>Auto-Updates</strong> - Launcher keeps itself and mods up to date</li>
  <li><strong>Easy Install</strong> - Players just run the exe</li>
  <li><strong>Simple Updates</strong> - Admins just update one zip file</li>
  <li><strong>Steam Integration</strong> - Automatically finds Valheim</li>
  <li><strong>Safe Updates</strong> - Verifies all mod files</li>
</ul>

<h2>📥 For Players</h2>
<ol>
  <li>Download <code>ValheimLauncher.zip</code> from <a href="../../releases">Releases</a></li>
  <li>Extract and run <code>ValheimLauncher.exe</code></li>
  <li>Click "Yes" when updates are available</li>
  <li>Play!</li>
</ol>

<blockquote>
  <strong>Note</strong>: You need BepInEx installed in your Valheim directory first
</blockquote>

<h2>🛠️ For Server Admins</h2>
<p>Want to use this for your own server? It's easy!</p>

<h3>Initial Setup</h3>
<ol>
  <li>Fork this repository</li>
  <li>Go to Actions tab in your fork</li>
  <li>Enable GitHub Actions</li>
  <li>Create your first release (see below)</li>
</ol>

<h3>Managing Mods</h3>
<h4>Prepare your mods:</h4>
<ul>
  <li>Collect your BepInEx plugins</li>
  <li>Create a zip named <code>plugins.zip</code></li>
  <li>Structure should match your BepInEx plugins folder</li>
</ul>

<h4>Update the mods:</h4>
<ul>
  <li>Upload <code>plugins.zip</code> to the <code>Mods</code> folder in your repo</li>
  <li>Commit and push</li>
  <li>Players will get the updates automatically</li>
</ul>

<h3>Creating Releases</h3>
<ol>
  <li>Go to Actions tab</li>
  <li>Click "Build and Release"</li>
  <li>Click "Run workflow"</li>
  <li>Choose version type:
    <ul>
      <li><code>patch</code> for small updates (1.0.0 → 1.0.1)</li>
      <li><code>minor</code> for new features (1.0.0 → 1.1.0)</li>
      <li><code>major</code> for big changes (1.0.0 → 2.0.0)</li>
    </ul>
  </li>
</ol>

<p>The workflow automatically:</p>
<ul>
  <li>Builds the launcher</li>
  <li>Creates a release</li>
  <li>Updates version numbers</li>
  <li>Uploads everything needed</li>
</ul>

<h3>Distribution</h3>
<ol>
  <li>Share the launcher with your players</li>
  <li>Update mods by replacing <code>plugins.zip</code></li>
  <li>Players get updates automatically</li>
</ol>

<h2>📋 Requirements</h2>

<h3>For Players</h3>
<ul>
  <li>Windows 10/11</li>
  <li>Valheim (Steam version)</li>
  <li>BepInEx installed</li>
</ul>

<h3>For Server Admins</h3>
<ul>
  <li>GitHub account</li>
  <li>Your server's mod files</li>
</ul>

<h2>❓ Common Issues</h2>

<p><strong>Q: Launcher can't find Valheim</strong><br>
A: Run as administrator</p>

<p><strong>Q: Mods not working</strong><br>
A: Make sure BepInEx is installed</p>

<p><strong>Q: Updates fail</strong><br>
A: Run as administrator, check antivirus</p>

<h2>🆘 Support</h2>

<p>Having problems?</p>
<ol>
  <li>Run as administrator</li>
  <li>Check BepInEx installation</li>
  <li>Check <a href="../../issues">Issues</a></li>
  <li>Create new issue with your launcher log</li>
</ol>

<hr>

<p align="center"><em>Made for Valheim server admins who just want to help their friends keep mods updated!</em></p>
</div>
