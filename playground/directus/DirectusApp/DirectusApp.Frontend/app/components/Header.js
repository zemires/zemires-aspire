"use client";

import Link from 'next/link';

export default function Header({ navigation }) {
    // Find the Main Navigation set by title
    const headerNavigation = navigation.filter((nav) => nav.title === 'Main Navigation')[0];

    if (!headerNavigation || headerNavigation.items?.length === 0) {
        return null;
    }

    return (
        <header className="site-header">
            <div className="container">
                <div className="logo">
                    <Link href="/">
                        <span className="logo-text">Your Site</span>
                    </Link>
                </div>

                <div className="main-nav">
                    {headerNavigation.items.map((item) => (
                        <NavigationItem key={item.id} item={item} />
                    ))}
                </div>
            </div>

            <style jsx>{`
        .site-header {
          background-color: #ffffff;
          box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
          position: sticky;
          top: 0;
          z-index: 100;
        }

        .container {
          max-width: 1200px;
          margin: 0 auto;
          padding: 0 20px;
          height: 80px;
          display: flex;
          align-items: center;
          justify-content: space-between;
        }

        .logo {
          font-size: 24px;
          font-weight: 700;
        }

        .logo-text {
          color: #3182ce;
          text-decoration: none;
        }

        .main-nav {
            display: flex;
            align-items: center;
            list-style: none;
            margin: 0;
            padding: 0;
            gap: 20px;
        }

        .main-nav a {
          color: #4a5568;
          text-decoration: none;
          font-size: 16px;
          font-weight: 500;
          transition: color 0.3s;
        }

        .main-nav a:hover {
          color: #3182ce;
        }

        @media (max-width: 768px) {
          .container {
            flex-direction: column;
            height: auto;
            padding: 20px;
          }

          .logo {
            margin-bottom: 20px;
          }

        }
      `}</style>
        </header>
    );
}

// Recursive navigation item renderer
function NavigationItem({ item }) {
    if (item.type === 'group') {
        return (
            item.children?.map((child) => (
                <NavigationItem key={child.id} item={child} />
            ))
        );
    }

    return (
        <div>
            <Link
                href={resolveItemUrl(item)}
                target={item.target === '_blank' ? '_blank' : undefined}
                rel={item.target === '_blank' ? 'noopener noreferrer' : undefined}
            >
                {item.title}
            </Link>
        </div>
    );
}

// Helper to resolve link destination properly
function resolveItemUrl(item) {
    if (item.type === 'page' && item.page) {
        return `${item.page.permalink}`;
    }
    if (item.type === 'post' && item.post) {
        return `/posts/${item.post}`;
    }
    if (item.type === 'url' && item.url) {
        return item.url;
    }
    return '#';
}
