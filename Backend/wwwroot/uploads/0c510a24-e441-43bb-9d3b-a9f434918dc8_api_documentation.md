# WhatsApp Campaign System - API Documentation

This document contains all the API routes, request bodies, and response structures needed by the frontend team to integrate with the backend. 

**Base URL:** `http://localhost:5155/api`
**Response Format:** All endpoints return a standardized wrapper:
```json
{
  "success": true,       // true or false
  "message": "...",      // Success or error message
  "data": { ... },       // The actual payload (object or array)
  "errors": null         // Array of validation errors if success=false
}
```

---

## 1. Dashboard API

### Get Dashboard Summary
Fetches aggregate statistics for the dashboard UI.
* **Route:** `GET /Dashboard/summary`
* **Response `data`:**
```json
{
  "totalContacts": 150,
  "totalCampaigns": 12,
  "messagesSent": 5000,
  "messagesDelivered": 4900,
  "messagesRead": 4500,
  "messagesFailed": 100,
  "recentCampaigns": [
    {
      "id": 1,
      "name": "Summer Promo",
      "status": "Sent",
      "totalRecipients": 1000,
      "deliveredCount": 990
    }
  ]
}
```

---

## 2. Contacts API

### Get All Contacts (Paginated)
* **Route:** `GET /Contacts?page=1&pageSize=10&search=john`
* **Query Params:** `page` (default 1), `pageSize` (default 10), `search` (optional)
* **Response `data`:**
```json
{
  "totalCount": 50,
  "page": 1,
  "pageSize": 10,
  "items": [
    {
      "id": 1,
      "name": "John Doe",
      "phone": "+919876543210",
      "type": "Customer",  // "Lead" or "Customer"
      "source": "Manual",
      "assignedTo": "Jane Smith",
      "isActive": true,
      "createdAt": "2026-07-07T10:00:00Z"
    }
  ]
}
```

### Get Contact by ID
* **Route:** `GET /Contacts/{id}`
* **Response `data`:** Returns the single contact object shown above.

### Create Contact
* **Route:** `POST /Contacts`
* **Request Body:**
```json
{
  "name": "John Doe",
  "phone": "+919876543210",
  "type": "Customer",
  "source": "Manual",
  "assignedTo": "Jane Smith",
  "groupIds": [1, 2] // Optional: IDs of groups to add this contact to
}
```
* **Response `data`:** Returns the created contact object.

### Update Contact
* **Route:** `PUT /Contacts/{id}`
* **Request Body:** Same as Create Contact.
* **Response `data`:** Returns the updated contact object.

### Delete Contact
* **Route:** `DELETE /Contacts/{id}`
* **Response:** Returns `success: true`.

---

## 3. Contact Groups API

### Get All Groups
* **Route:** `GET /ContactGroups?page=1&pageSize=10`
* **Response `data`:**
```json
{
  "totalCount": 5,
  "page": 1,
  "pageSize": 10,
  "items": [
    {
      "id": 1,
      "name": "VIP Customers",
      "description": "High value clients",
      "memberCount": 42,
      "createdAt": "2026-07-07T10:00:00Z"
    }
  ]
}
```

### Create Group
* **Route:** `POST /ContactGroups`
* **Request Body:**
```json
{
  "name": "VIP Customers",
  "description": "High value clients"
}
```
* **Response `data`:** Returns the created group object.

### Update Group
* **Route:** `PUT /ContactGroups/{id}`
* **Request Body:** Same as Create Group.
* **Response `data`:** Returns the updated group object.

### Add Members to Group
* **Route:** `POST /ContactGroups/{groupId}/members`
* **Request Body:**
```json
{
  "contactIds": [1, 2, 3]
}
```
* **Response:** Returns `success: true`.

### Remove Members from Group
* **Route:** `DELETE /ContactGroups/{groupId}/members`
* **Request Body:**
```json
{
  "contactIds": [1, 2]
}
```
* **Response:** Returns `success: true`.

---

## 4. Templates API

### Sync Templates from Meta
Fetches the latest templates directly from your Meta Business account.
* **Route:** `POST /Templates/sync`
* **Response:** Returns `success: true` indicating sync completion.

### Get All Templates
Fetches the synchronized templates from your database to display in the UI dropdowns.
* **Route:** `GET /Templates`
* **Response `data`:** Array of templates.
```json
[
  {
    "id": 1,
    "whatsappTemplateId": "12345",
    "name": "camp_platinum_credit_card_1",
    "language": "en",
    "category": "MARKETING",
    "status": "Approved",
    "bodyText": "Apply for our Platinum Credit Card today!...",
    "variables": ["@name", "1", "2"]
  }
]
```

---

## 5. Campaigns API

### Get All Campaigns
* **Route:** `GET /Campaigns?page=1&pageSize=10`
* **Response `data`:**
```json
{
  "totalCount": 10,
  "page": 1,
  "pageSize": 10,
  "items": [
    {
      "id": 1,
      "name": "Summer Promo",
      "templateName": "camp_platinum_credit_card_1",
      "relationType": "Lead",
      "scheduleType": "Immediate",
      "status": "Sent",
      "totalRecipients": 500,
      "deliveredCount": 490,
      "readCount": 400,
      "failedCount": 10
    }
  ]
}
```

### Get Campaign Details & Recipients
* **Route:** `GET /Campaigns/{id}`
* **Response `data`:** Returns campaign object with an extra array of recipients.
```json
{
  // ... campaign fields above ...
  "recipients": [
    {
      "contactId": 1,
      "contactName": "John Doe",
      "phone": "+919876543210",
      "status": "Read",
      "sentAt": "2026-07-07T10:00:00Z",
      "deliveredAt": "2026-07-07T10:00:05Z",
      "readAt": "2026-07-07T10:05:00Z",
      "errorMessage": null
    }
  ]
}
```

### Create and Send/Schedule Campaign
* **Route:** `POST /Campaigns`
* **Request Body:**
```json
{
  "name": "My First Campaign",
  "templateId": 1, 
  "relationType": "Lead",         // "All", "Lead", or "Customer"
  "scheduleType": "Immediate",    // "Immediate" or "Scheduled"
  "scheduledAt": null,            // UTC Date string if Scheduled
  "contactIds": [1, 2],           // Array of specific contact IDs (optional)
  "groupIds": [1],                // Array of Group IDs (optional)
  "variables": []                 // IMPORTANT: Must be empty array [] if template has no variables!

}
```
* **Response `data`:** Returns the created campaign object.

### Cancel Scheduled Campaign
* **Route:** `POST /Campaigns/{id}/cancel`
* **Response `data`:** Returns the updated campaign object with status `Cancelled`.

### Delete Campaign
Only drafts, failed, or cancelled campaigns can be deleted.
* **Route:** `DELETE /Campaigns/{id}`
* **Response:** Returns `success: true`.
