import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AccountService } from './core/account.service';
import { compactGuid, expandGuid } from './core/guid';
import { AdminPageComponent } from './features/admin/admin-page.component';
import { ListPageComponent } from './features/lists/list-page.component';

/**
 * The bare root has no account in it, so it mints one and forwards to a real
 * bookmarkable address. This is the only place an account is ever created.
 */
const newAccount: CanActivateFn = async () => {
  const router = inject(Router);

  try {
    const { key, lists } = await inject(AccountService).createAccount();
    return router.createUrlTree(['/user', compactGuid(key), 'list', compactGuid(lists[0].id)]);
  } catch {
    return router.createUrlTree(['/unavailable']);
  }
};

/**
 * Points the app at the account in the URL. Runs synchronously so the HTTP
 * interceptor has the key before the page issues its first request.
 */
const useAccount: CanActivateFn = (route) => {
  const key = route.paramMap.get('accountKey');
  if (!key) {
    return inject(Router).createUrlTree(['/']);
  }

  inject(AccountService).use(expandGuid(key));
  return true;
};

/** /user/{key} with no list picks the account's first one. */
const firstList: CanActivateFn = async (route) => {
  const router = inject(Router);
  const key = route.parent?.paramMap.get('accountKey') ?? '';

  const lists = await inject(AccountService).loadLists();
  return lists.length > 0
    ? router.createUrlTree(['/user', key, 'list', compactGuid(lists[0].id)])
    : router.createUrlTree(['/unavailable']);
};

/** The admin page is only for keys listed under Admin:Keys on the server. */
const adminOnly: CanActivateFn = async () => {
  const router = inject(Router);
  const account = inject(AccountService);

  const lists = await account.loadLists();
  if (account.isAdmin()) {
    return true;
  }

  // A non-admin lands on their first list, or the unavailable page if the key
  // did not resolve to anything at all.
  const key = compactGuid(account.key());
  return lists.length > 0
    ? router.createUrlTree(['/user', key, 'list', compactGuid(lists[0].id)])
    : router.createUrlTree(['/unavailable']);
};

export const routes: Routes = [
  { path: '', canActivate: [newAccount], children: [] },
  {
    path: 'user/:accountKey',
    canActivate: [useAccount],
    children: [
      { path: '', canActivate: [firstList], children: [] },
      { path: 'list/:listId', component: ListPageComponent },
      { path: 'admin', canActivate: [adminOnly], component: AdminPageComponent },
    ],
  },
  { path: 'unavailable', component: ListPageComponent },
  { path: '**', redirectTo: '' },
];
