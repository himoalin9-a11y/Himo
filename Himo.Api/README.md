# Himo.Api

Backend API for Himo.

## v21
- Messages now include `SenderUserId` in API responses.
- The MAUI client polls the active conversation every 2 seconds while it is open.
- Incoming messages are identified using the signed-in phone number and stored with their remote message id to prevent duplicates.

Development OTP remains `123456` and must be replaced with a real SMS provider before production.


## v48
- API is explicitly bound to `http://0.0.0.0:5080` so physical Android devices on the same LAN can connect.
- Android emulator can continue using `http://10.0.2.2:5080/`.
- Physical phone should use the PC LAN address, for example `http://192.168.8.85:5080/`.


## External production database

The hosted API uses Supabase PostgreSQL through the `DATABASE_URL` environment variable. SQLite is no longer used by the API. Set `DATABASE_URL` only in Render Environment Variables; do not place the database password in the Android app or source repository. The `/health` endpoint reports the database connection state.
