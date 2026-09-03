import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { userTokenInterceptor } from './core/user-token.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding lets ListPageComponent take :listId as an input,
    // so switching lists is a plain navigation.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([userTokenInterceptor])),
  ],
};
