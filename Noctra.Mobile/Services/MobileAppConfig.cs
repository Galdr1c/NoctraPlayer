namespace Noctra.Mobile.Services;

/// <summary>
/// Mobile app compile-time configuration.
/// Environment variables are not easily accessible on mobile platforms,
/// so we use compile-time constants instead.
/// </summary>
public static class MobileAppConfig
{
    // ========== DEVELOPMENT TESTING ==========
    // For local testing, you can use:
    // 1. A test file in project root: "file:///path/to/test-promo-codes.json"
    // 2. A development server: "http://localhost:8000/test-promo-codes.json"
    // 3. A public test URL: "https://raw.githubusercontent.com/your-repo/test-promo-codes.json"
    
    // ========== PRODUCTION ==========
    // Before production build, update to your actual promo codes endpoint:
    // Example: "https://api.kynora.studio/noctra/promo-codes.json"
    
    // Test codes in test-promo-codes.json:
    // - MOBILE-TEST-7D (7 days, reusable)
    // - MOBILE-TEST-30D (30 days, reusable)
    // - NOCTRA-PREMIUM-90D (90 days, single use)
}
