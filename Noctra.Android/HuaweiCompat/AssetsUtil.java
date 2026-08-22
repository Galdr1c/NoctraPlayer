package com.huawei.hms.framework.common;

import android.content.Context;

import java.io.IOException;

/**
 * Compatibility bridge for the Huawei NetworkGrs binding.
 *
 * NetworkGrs 6.0.4 calls this helper when it reads its bundled route
 * configuration, but the Xamarin NetworkCommon binding does not package it.
 */
public final class AssetsUtil {
    private AssetsUtil() {
    }

    public static String[] list(Context context, String path) {
        if (context == null) {
            return new String[0];
        }

        try {
            String[] entries = context.getAssets().list(path);
            return entries == null ? new String[0] : entries;
        } catch (IOException ignored) {
            return new String[0];
        }
    }
}
