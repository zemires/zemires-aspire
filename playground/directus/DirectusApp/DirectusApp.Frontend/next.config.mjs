/** @type {import('next').NextConfig} */
const nextConfig = {
  images: {
	//domains: ['localhost'],
  dangerouslyAllowLocalIP: true,
    remotePatterns: [
      {
        protocol: 'http',
        hostname: 'localhost',
        port: '8055',
        pathname: '/assets/**',
      },
    ],
  },
};

export default nextConfig;
