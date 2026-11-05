<script setup lang="ts">
import { getDecodedToken } from '@/lib/utils'
import { IniaIcon } from '@/components/atoms'

const tokenParsed = getDecodedToken()
const { preferred_username, email } = tokenParsed || {}
</script>

<template>
  <div class="profile-details">
    <div class="detail-card">
      <div class="detail-row">
        <div class="detail-label">
          <IniaIcon name="user" class="detail-icon" />
          <span>User Name:</span>
        </div>
        <p class="detail-value">{{ preferred_username }}</p>
      </div>

      <div class="detail-row">
        <div class="detail-label">
          <IniaIcon name="envelope" class="detail-icon" />
          <span>Email:</span>
        </div>
        <p class="detail-value">{{ email }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.profile-details {
  width: 100%;
}

.detail-card {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: var(--spacing-lg);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.detail-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-lg);
  padding: var(--spacing-sm) 0;
}

.detail-label {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  min-width: 176px;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-secondary);
}

.detail-icon {
  font-size: 16px;
  color: var(--accent-indigo-from);
}

.detail-value {
  font-size: 14px;
  color: var(--text-primary);
  font-weight: 500;
}

@media (max-width: 768px) {
  .detail-row {
    flex-direction: column;
    align-items: flex-start;
    gap: var(--spacing-xs);
  }

  .detail-label {
    min-width: auto;
  }
}
</style>
