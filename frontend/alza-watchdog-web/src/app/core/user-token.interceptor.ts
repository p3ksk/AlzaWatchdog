import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AccountService } from './account.service';

/**
 * Attaches the account key to every API call. The key comes from the URL, put
 * there by the route guard before any request is issued.
 */
export const userTokenInterceptor: HttpInterceptorFn = (req, next) => {
  const key = inject(AccountService).key();

  if (!key || !req.url.startsWith('/api/')) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { 'X-User-Token': key } }));
};
