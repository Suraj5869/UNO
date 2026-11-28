import { test, expect } from '@playwright/test';

test('verification recovery is shown only after an unverified login', async ({ page }) => {
  await page.route('**/api/auth/refresh', route => route.fulfill({ status: 401 }));
  let unverified = false;
  await page.route('**/api/auth/login', route => route.fulfill({ status: unverified ? 403 : 401, json: unverified ? { code: 'EMAIL_NOT_VERIFIED', email: 'player@example.com', verificationRequested: false } : { message: 'Invalid credentials.' } }));
  await page.route('**/api/auth/resend-verification', route => route.fulfill({ status: 202, json: {} }));
  await page.goto('/');
  await expect(page.getByRole('button', { name: /send verification email/i })).toHaveCount(0);
  await page.getByLabel('Email or username').fill('player');
  await page.getByPlaceholder('Enter your password').fill('test-password');
  await page.getByRole('button', { name: 'Sign In', exact: true }).click();
  await expect(page.getByText('Invalid credentials.')).toBeVisible();
  await expect(page.getByRole('button', { name: /send verification email/i })).toHaveCount(0);
  unverified = true;
  await page.getByRole('button', { name: 'Sign In', exact: true }).click();
  await expect(page.getByText('Email not verified', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Send verification email', exact: true }).click();
  await expect(page.getByRole('button', { name: /Resend email in/ })).toBeDisabled();
  await page.getByLabel('Email or username').fill('another');
  await expect(page.getByText('Email not verified', { exact: true })).toHaveCount(0);
});

test('signup opens a dedicated verification screen', async ({ page }) => {
  await page.route('**/api/auth/refresh', route => route.fulfill({ status: 401 }));
  await page.route('**/api/auth/register', route => route.fulfill({ status: 202, json: {} }));
  await page.goto('/');
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  await page.getByLabel('Email', { exact: true }).fill('player@example.com');
  await page.getByLabel('Username', { exact: true }).fill('player');
  await page.getByLabel('Display name').fill('Player');
  await page.getByPlaceholder('Enter your password').fill('test-password');
  await page.getByLabel('Confirm password').fill('test-password');
  await page.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Check your email' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Resend email in/ })).toBeDisabled();
  await page.getByRole('button', { name: 'Back to Sign In' }).click();
  await expect(page.getByRole('button', { name: /send verification email/i })).toHaveCount(0);
});
