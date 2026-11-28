import React from 'react';
import { createRoot } from 'react-dom/client';
import AuthGate from '../../src/AuthGate';
import { MatchReward } from '../../src/ProgressionPanel';

export function mountReward() {
  const host = document.createElement('div');
  host.id = 'reward-test-root';
  document.body.appendChild(host);
  createRoot(host).render(
    <React.StrictMode>
      <AuthGate>{(player, _logout, update) => player ? <MatchReward matchId="match-test" player={player} onUpdate={update} /> : null}</AuthGate>
    </React.StrictMode>
  );
}
