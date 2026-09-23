# Stage 57 — Always-On LAN Server

The Himo API is prepared to run continuously on a Windows server PC as a Windows Service.

Included:
- Windows Service hosting via Microsoft.Extensions.Hosting.WindowsServices.
- Self-contained win-x64 publish script.
- Administrator install script.
- Automatic Windows Firewall rule for TCP 5080 on Private networks.
- Automatic service startup with Windows.
- Persistent SQLite/App_Data storage under C:\HimoServer when installed.
- LAN setup and Android API URL instructions.

Server endpoint:
http://SERVER-IP:5080/
Health:
http://SERVER-IP:5080/health

Important:
The server PC must stay powered on and connected to the LAN. Reserve its LAN IP in the router so the Android clients keep a stable API address.
