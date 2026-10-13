import { createDirectus, rest, authentication, staticToken } from '@directus/sdk';
const BACKEND_URL = process.env.DIRECTUS_URI; //"http://localhost:8055/"
const TOKEN = process.env.DIRECTUS_STATIC_TOKEN;
const client = createDirectus(BACKEND_URL)
    //.with(authentication("json"))
    .with(staticToken(TOKEN))
    .with(rest({
        onRequest: (options) => ({ ...options, cache: 'no-store' }),
  }))
export default client;
