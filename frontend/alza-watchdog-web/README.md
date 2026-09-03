# alza-watchdog-web

Angular frontend for Alza Watchdog. See the [root README](../../README.md) for
what this is and how to run the whole thing.

```bash
npm start        # dev server on :4200, proxying /api to the API on :5080
npm run build    # production build into dist/
```

The API must be running for anything to load — the app asks it for a GUID during
initialisation.
