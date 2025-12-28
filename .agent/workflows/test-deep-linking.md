---
description: Test Deep Linking with Expo Go
---

# Testing Deep Linking with Expo Go

This guide helps you verify the email verification deep link.

## 1. Configure Server

1. Open `Ping-server/appsettings.json`.
2. Find the key `"ClientUrl"`.
3. Check your terminal where `npx expo start` is running. You should see something like:
   ```
   Expo Go: exp://192.168.1.5:8081
   ```
4. Copy that URL and paste it into `appsettings.json`:
   ```json
   "ClientUrl": "exp://192.168.1.5:8081",
   ```
5. Restart your .NET server:
   ```powershell
   # In Ping-server terminal
   Ctrl+C
   dotnet run
   ```

## 2. Configure Client (Expo App)

 Ensure your `app/(auth)/verify-email-code.tsx` can handle query parameters.

```typescript
import { useLocalSearchParams } from 'expo-router';
// ... inside component
const { code, email } = useLocalSearchParams();

useEffect(() => {
  if (code && email) {
    // Auto-fill form or trigger verification automatically
    setForm({ ...form, code: code as string });
  }
}, [code, email]);
```

## 3. Test Flow

1. **Sign Up**: Register a new user in the app.
2. **Check Email**: Look at your server logs or your actual email (if configured with SES).
   - In Development logs, you might see the HTML content.
   - If using `smtp4dev` or similar, check the mailbox.
3. **Click Link**: Tap the "Verify Email" link on your phone.
   - It should ask to open with "Expo Go".
   - It should navigate to `/verify-email-code` and pre-fill the code.
