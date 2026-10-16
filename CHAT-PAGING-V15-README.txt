Himo - Chat Paging V15
======================

This stage replaces the previous full-history chat loading behavior with a
local-first, cursor-based chat timeline:

1) Opening a chat shows only the newest 30 messages.
2) The UI starts at the newest message at the bottom.
3) Scrolling to the top loads the next older page (30 messages).
4) Older pages are taken from the local cache first; the server is queried only
   when local history is exhausted.
5) New incoming/sent messages are appended to the end of the visible list and
   do not rebuild the entire CollectionView.
6) The server now supports limit + beforeId cursor paging and has a message
   (ConversationId, SentAt, Id) index for faster page reads.

IMPORTANT:
The Himo client and Himo.Api must be rebuilt from this same project. The API
changes must also be deployed to the server used by the phone (for example
Render) before testing server-side paging.

Build client:
- Clean Solution
- delete bin and obj
- Rebuild Solution

Then deploy/restart Himo.Api and install the rebuilt Android app.
