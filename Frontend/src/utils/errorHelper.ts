/**
 * Translation helper for formatting raw Axios response validation error details
 * as a clean, user-friendly error string.
 */
export const getErrorMessage = (err: any, fallbackMessage: string): string => {
  if (!err) return fallbackMessage;

  const data = err?.response?.data;
  if (!data) return err.message || fallbackMessage;

  // 1. Check custom ApiResponse message
  if (data.message && data.message !== "Validation failed" && data.message !== "Validation failed.") {
    return data.message;
  }

  // 2. Check ApiResponse errors array
  if (Array.isArray(data.errors) && data.errors.length > 0) {
    return data.errors[0];
  }

  // 3. Check standard ASP.NET Core ValidationProblemDetails dictionary
  if (data.errors && typeof data.errors === 'object') {
    const errorList = Object.values(data.errors);
    if (errorList.length > 0) {
      const messages = errorList[0];
      if (Array.isArray(messages) && messages.length > 0) {
        return messages[0];
      }
      if (typeof messages === 'string') {
        return messages;
      }
    }
  }

  // 4. Fallback to general title / message
  return data.title || data.message || err.message || fallbackMessage;
}
