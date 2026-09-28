import { bootstrapApplication } from '@angular/platform-browser';
import { loadRuntimeConfig, renderBootstrapError } from '@veilla/shared';
import { App } from './app/app';
import { buildAppConfig } from './app/app.config';

// La configuration (API, OIDC) est lue à l'exécution depuis assets/config.json (CTR-05).
loadRuntimeConfig()
  .then((config) => bootstrapApplication(App, buildAppConfig(config)))
  .catch((error: unknown) => renderBootstrapError(error));
