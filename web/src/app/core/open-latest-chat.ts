import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { CHAT_STORE } from './chat-store';

/**
 * Landing behaviour: pick up where the user left off, or start a fresh chat.
 * A guard rather than a component so `/` never renders — it always redirects to
 * a real chat URL, which keeps every visible state addressable.
 */
export const openLatestChat: CanActivateFn = () => {
  const store = inject(CHAT_STORE);
  const router = inject(Router);

  const latest = store.chats()[0] ?? store.createChat();
  return router.createUrlTree(['/chat', latest.id]);
};
