# Windows productivity controls

The first Productivity bank is active in `LOOP` → `Productivity` → `Windows`. The footer reads `WINDOWS` while the bank is live.

| SCS.3d control | Windows action |
| --- | --- |
| Main ring, top/right/bottom/left | Arrow-key navigation |
| GAIN strip | Vertical scrolling |
| PLAY | Enter / activate |
| CUE | Escape / back |
| SYNC | Switch to the next application |
| TAP | Move to the next field |
| B11 | Undo |
| B12 | Redo |
| B13 | Copy |
| B14 | Paste |

The ring is divided into four broad directional sectors and rate-limited to prevent the controller's dense touch stream from flooding the foreground application. Moving within or holding a sector repeats navigation at a controlled pace. All shortcuts are sent as complete press/release batches, so changing modes, pausing routing, or using Safe Stop cannot leave Ctrl or Alt held down.

## Browser bank

Press `LOOP` again while Productivity is selected to open the `Browser` bank. The ring and GAIN strip keep the same navigation and scrolling behavior, while the buttons become browser-safe controls:

| SCS.3d control | Browser action |
| --- | --- |
| Main ring, top/right/bottom/left | Arrow-key navigation |
| GAIN strip | Vertical scrolling |
| PLAY | Enter / activate link |
| CUE | Back |
| SYNC | Forward |
| TAP | Refresh page |
| B11 | Previous tab |
| B12 | Next tab |
| B13 | Focus address bar |
| B14 | Find on page |

The bank intentionally does not include a close-tab shortcut, reducing the chance of an accidental destructive action.

## Meetings bank

Press `LOOP` a third time for `Meetings`. Its microphone controls operate on the Windows default input endpoint, so they do not depend on a particular meeting application's shortcut scheme.

| SCS.3d control | Meetings action |
| --- | --- |
| Main ring, top/right/bottom/left | Arrow-key navigation |
| GAIN strip | Set default microphone level |
| PLAY | Enter / activate focused control |
| CUE | Escape / dismiss a meeting panel |
| SYNC | Switch applications |
| TAP | Move to the next meeting control |
| B11 | Microphone level down 5% |
| B12 | Microphone level up 5% |
| B13 | Toggle default microphone mute |
| B14 | Force default microphone live |

B13 remains lit while the endpoint is muted, both in the companion and on the controller. Applications configured to use a different input device or an exclusive audio path may not follow the Windows default endpoint. Streaming remains a planned bank.
