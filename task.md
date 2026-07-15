# Execution Checklist

## Backend Changes
- [x] Modify `Campaign.cs` to add missing media properties (`FileName`, `FileType`, `FileUrl`).
- [x] Update `IChatService.cs` interface with `GetOrCreateConversationAsync` and updated `CreateOrUpdateCampaignMessageAsync` signature.
- [x] Update `ChatService.cs` implementation.
- [x] Update `CampaignService.cs` to store file variables and handle attachments during message delivery (sending separate media messages for text templates).

## Frontend Changes
- [x] Add state `showTimeBanner` and compute `lastActiveMessage` and `windowStatus` in `Chat.tsx`.
- [x] Implement green glowing dot, yellow expired warning banner, and green remaining time banner in `Chat.tsx`.
- [x] Style the glowing dot, remaining time banner, and limit banner in `Chat.css`.

## Verification
- [x] Build backend and frontend to verify compile success.
- [x] Verify message logic and campaigns delivery.
- [x] Align style sheet spacings, buttons, headers, and Group not found badges in ContactsList.css
- [x] Build and verify everything compiles and runs successfully
