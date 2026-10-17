# Writing and sharing Bunker widgets

A **community widget** adds buttons and fields to Bunker. It is a single `.json` file. It is **never code**: it can only
send the console commands written on its buttons, and Bunker shows you every one of them before you install it.

## Install a widget

In Bunker click **Get more widgets...** at the bottom of the **WIDGETS** list in the sidebar, then:

| Button | What it does |
| --- | --- |
| **Browse library** | Lists the widgets published in this repository's [`widgets/`](../widgets) folder. |
| **Add from file…** | Installs a `.json` file from your PC. |
| **Add from link…** | Installs from an `https://` link, for example a raw GitHub file. |

Every install shows a review window with the author, the description and **every command the widget can send**.
Commands that can affect players or the server itself (kick, ban, restart, give-to-everyone…) are marked with `!`.

Installed widgets appear in the **WIDGETS** list in the sidebar (with a green dot, and a Community tag on their page) and in the picker on the **Layout** page.

## The file format

```json
{
  "id": "resource-drop",
  "name": "Resource Drop",
  "author": "Your name",
  "version": "1.0.0",
  "description": "Reward everyone online with a resource.",
  "controls": [
    { "type": "choice", "id": "item", "label": "Resource", "options": ["wood", "stones", "scrap"] },
    { "type": "number", "id": "amount", "label": "Amount each", "default": 500, "min": 1, "max": 5000 },
    {
      "type": "button",
      "text": "Give to everyone",
      "confirm": "Give {amount} x {item} to EVERY player?",
      "commands": [
        "inventory.giveall {item} {amount}",
        "say \"Everyone just received {amount} {item}!\""
      ]
    }
  ]
}
```

### Top level

| Field | Required | Notes |
| --- | --- | --- |
| `id` | yes | 2–41 characters: lower-case letters, numbers, `-` and `_`. Must be unique. |
| `name` | yes | Up to 40 characters. Shown on the tab and in the Layout picker. |
| `author`, `version`, `description` | no | Shown in the review window and the library. |
| `controls` | yes | 1–30 controls, shown top to bottom. |

### Controls

| `type` | Fields | What the user sees |
| --- | --- | --- |
| `label` | `text` | A line of explanatory text. |
| `separator` | – | A thin divider line. |
| `text` | `id`, `label`, `placeholder` | A single-line text box. |
| `number` | `id`, `label`, `default`, `min`, `max` | A number box. The value is checked against `min` and `max`. |
| `choice` | `id`, `label`, `options` | A drop-down list (1–50 options). |
| `player` | `id`, `label` | A drop-down of the players online right now. |
| `button` | `text`, `commands`, `style`, `confirm`, `output` | Runs 1–20 commands. `style` is `primary` (default) or `ghost`. `confirm` asks first. `output` sends the server's replies to an `output` control. |
| `output` | `id`, `height`, `format`, `pick` | A read-only panel that shows what the server answered (60–600 px high, default 160). Buttons point at it with `"output": "<id>"`. Long lines are kept whole and scroll sideways. `format` is `text` (default) or `plugins`; see below. |
| `stat` | `label`, `source` | A live number from the server. `source` is one of `players`, `maxplayers`, `fps`, `entities`, `memory`, `uptime`, `queued`, `joining`, `map`. |

Consecutive buttons are placed side by side.

### Timed steps

A command that is just `wait N` pauses for N seconds before the next command, and is **not** sent to the server. Use it for countdowns and staged announcements:

```json
{
  "type": "button",
  "text": "1 minute countdown",
  "commands": [
    "say \"Event starts in 1 minute!\"",
    "wait 30",
    "say \"30 seconds!\"",
    "wait 30",
    "say \"Go!\""
  ]
}
```

While a button has timed steps running it turns into **Stop**, and pressing it cancels the rest. One wait can be at most 3600 seconds, and all the waits in one button together at most 900 seconds (15 minutes).

### Showing the server's replies

Add an `output` control and point buttons at it. Bunker shows each command and the server's reply there, in addition to the normal console:

```json
{ "type": "output", "id": "report", "height": 200 },
{ "type": "button", "text": "Status", "output": "report", "commands": [ "status" ] }
```

Bunker never tries to interpret a reply. It shows the text exactly as the server sent it.

### Placeholders

Inside a button's `commands` (and `confirm`) text, `{id}` is replaced by what the user entered in the control with that `id`.

* `{target}` for a `player` control is the player's **Steam ID**; `{target.name}` is their **name**.
* Text is cleaned first: quotes, semicolons, backslashes and control characters are removed, and it is cut to 200 characters.
* Numbers are checked against `min`/`max`; a `choice` must be one of its `options`.
* If a field is empty, the button refuses to run and tells the user which field to fill in.

### Plugin lists as a table

Set `"format": "plugins"` on an `output` control and a Carbon (`c.plugins`) or Oxide (`oxide.plugins`) plugin list is shown as a sortable table: click a heading to sort, and failed or unloaded plugins are listed underneath. Add `"pick": "<id of a text control>"` and clicking a row fills that field with the plugin name. The same table is used for the server's ban list (`banlistex`): Steam ID, name, reason and when each ban ends, and a click on a row picks the Steam ID. Anything that is not a plugin list or a ban list is shown as plain text, and **Show raw reply** is always one click away.

```json
{ "type": "output", "id": "plugins", "format": "plugins", "pick": "plugin", "height": 380 },
{ "type": "text", "id": "plugin", "label": "Plugin name" },
{ "type": "button", "text": "List plugins", "output": "plugins", "commands": [ "c.plugins" ] }
```

Carbon's layout was checked against a real server reply. Oxide's is the commonly documented one and has not been checked on a live server.

## What widgets can not do

* They are data, not programs: no scripts, no network access, no files.
* They can not use commands that change RCON itself or who is an admin: anything starting `rcon.`, and
  `ownerid`, `moderatorid`, `removeowner`, `removemoderator`. Bunker refuses to install a widget that contains one.
* Every command is visible in the review window before installing, and marked `!` if it is a risky kind.

## Share your widget

The easiest way is from inside Bunker. You need a free GitHub account.

1. Open **Widgets → Get more widgets...** and press **Share a widget...**.
2. Choose one of your installed widgets, or a `.json` file from your PC. Bunker checks it and lists every command it can send.
3. Add a note for the reviewer if you like, then press **Submit on GitHub**. Your browser opens the submission page with the widget already filled in. Press **Submit new issue**.
   (A widget that is too long to fit in a web address is put on your clipboard instead: paste it into the **Widget file** box.)

What happens next:

* A check runs on GitHub within a minute or two and replies on your submission. It uses the same rules as the app, plus these for the library:
  the widget needs an `author`, a `version` like `1.0.0`, and a `description`; its `id` can't already be in the library unless your
  `version` is higher (that is how you publish an update).
* If something needs fixing, the reply says what. Edit the widget file in your submission and it is checked again.
* A person then reads the widget, command by command. If it is accepted it is added to [`widgets/`](../widgets), and it shows up in
  **Browse library** for every Bunker user straight away. People who already have it see **Update available** when you publish a newer version.

You can also open the form yourself: [Share a widget](https://github.com/Russ773/Bunker-Windows/issues/new?template=widget-submission.yml).
Or, if you prefer, fork the repository, add your file to [`widgets/`](../widgets) (under 64 KB) and add its file name to
[`widgets/index.json`](../widgets/index.json), then open a pull request that says what the widget does and which Rust commands it uses.

Anyone can still install widgets from other places using a link. Always read the command list before you install a widget you did not write.
