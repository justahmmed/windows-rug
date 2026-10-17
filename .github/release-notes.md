## Windows Rug v1.4.0

A living room, a tidy robot, and a fix for the rug vanishing.

![The living room at night](https://raw.githubusercontent.com/justahmmed/windows-rug/main/docs/room.jpg)

### What's new

- **A living room.** A corner sofa, a coffee table with a mug of coffee, a floor lamp and a monstera, all seen from above and lit like the rug. Drag them anywhere; right-click to turn or remove them; add or remove pieces from *Room* in the rug's menu.
- **A real lamp.** Click it to switch it on. Its warm light pools on the floor and falls on the rug, and it matters most at night.
- **The robot tidies up.** It picks up desktop icons lying around, carries them to the nearest rug and pushes them underneath. It also vacuums up any lost things left in the open. Turn it off with *Robot tidies icons*.
- **Lost things stay where they fell.** Drag the rug away and the coin stays on the floor. Click it to pick it up.

### Fixes

- **The rug could vanish after a double-click.** On some Windows versions the window could slip *under* the desktop. Every window now stays just above the desktop icons, never below them.
- **Never lost again.** A tray icon and **Ctrl+Alt+R** bring every rug back in front of you. A rug with broken numbers resets itself instead of disappearing, and any error is logged to `%APPDATA%\WindowsRug\error.log` instead of closing the app.

### Install

1. Download **WindowsRug.zip** below and extract it.
2. Double-click **Install.bat**. It replaces any older version.
