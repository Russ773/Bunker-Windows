# Privacy policy

Bunker runs on your own PC (Windows) or your own phone (Android). It doesn't collect, store on any server, or share personal information, and it has no analytics, advertising or tracking. There are no accounts.

**What stays on your device**
- Your settings, saved server details and the player database (with your notes) that Bunker builds from your own server. On Windows they are kept in your Windows user profile. On Android they are kept in the app's private storage and are left out of Android backups and device transfers.
- RCON passwords are encrypted. On Windows that uses Windows DPAPI, so only your Windows account can read them. On Android the key is held in the Android Keystore and never leaves the phone.
- Exports (the console log, the player list as a CSV file, the chat plugin) are only made when you choose Save or Share, and go where you send them.
- Removing the app removes its data. The Windows uninstaller asks first; on Android, uninstalling deletes everything.

**Where Bunker connects**
- **Your own Rust server**, at the address and port you enter, over WebRCON.
- **GitHub** (github.com and raw.githubusercontent.com). The Windows app uses it to check for updates in the downloadable version and to browse the community widget library when you open it. Both apps use it to fetch the Rust item list and item pictures (from the open bobosneefdev/rust-items-json project) so item search stays current. The item list is checked at most once a day, pictures are fetched as you search, and this can be switched off in the settings. GitHub's privacy policy applies to those requests.

**Android permissions**
- **Internet**, to reach your server and GitHub as described above.
- **Notifications**, to tell you when the connection drops or a chat message contains one of your watch-words. Each kind can be switched off.
- **Staying connected in the background** shows a quiet notification while you are on a server, so Android keeps the connection open with the screen off. It stops when you leave the server or switch it off in Settings.
- **Lock Bunker** (optional) uses Android's own fingerprint, face or screen lock prompt. Bunker never receives your fingerprint or face data.

Bunker sends nothing else to anyone. Source code: https://github.com/Russ773/Bunker-Windows

Questions: open an issue at https://github.com/Russ773/Bunker-Windows/issues
