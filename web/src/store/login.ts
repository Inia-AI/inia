import { defineStore } from 'pinia'
import { ref } from 'vue'

export default defineStore('login', () => {
  const creds = ref({})

  return { creds }
})
