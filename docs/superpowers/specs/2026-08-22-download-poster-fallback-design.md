# Download Poster Fallback Design

## Scope

Add the theme-aware Noctra full logo as a background placeholder whenever a downloaded Library series or movie, or a Series Detail main/episode item, has no usable poster. Apply the same visual behavior to desktop and mobile Library and Series Detail views only.

## Behavior

- The fallback Image is placed before RemoteImage in the same poster Grid.
- RemoteImage remains on top and keeps its existing loading/failure behavior; its zero opacity when no image is available reveals the fallback.
- The fallback uses LogoThemeConverter with ConverterParameter=Full, 32x32 dimensions for poster slots (24x24 in the 32x32 queue slot), Uniform stretch, 0.12 opacity, and IsHitTestVisible=false.
- Download Center Active, Queue, and Failed templates are intentionally unchanged; their existing status icons remain the only fallback.
- EpBgLayer pressed/hover backgrounds and player episode lists are out of scope.

## Compatibility

No model, converter, or RemoteImage changes are required. Desktop and mobile already register their platform-specific LogoThemeConverter resources.