# Download Poster Fallback Implementation Plan

1. Add failing source regression tests requiring two theme-aware fallback Image layers in each platform Downloads view and two in each platform Series Detail view, with none in Download Center.
2. Run the focused test and confirm it fails because the current views have no logo fallback layers.
3. Add the fallback Image before RemoteImage in desktop and mobile Library and Series Detail poster Grids; leave Active, Queue, and Failed cards unchanged.
4. Run focused regression tests, full Noctra.Tests, and desktop/Android builds.
