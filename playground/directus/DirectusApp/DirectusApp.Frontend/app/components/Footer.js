"use client";

import Link from 'next/link';

export default function Footer({ navigation }) {
  // Find the Footer Navigation set by title
  const footerNavigation = navigation?.filter((nav) => nav.title === 'Footer Navigation')[0];

  if (!footerNavigation || !footerNavigation.items?.length) {
    return null;
  }

  return (
    <footer className="site-footer">
      <div className="container">
        <div className="footer-content">
          {footerNavigation.items.map((item) => (
            <FooterNavigationItem key={item.id} item={item} />
          ))}
        </div>

        <div className="footer-bottom">
          <p>&copy; {new Date().getFullYear()} Your Site. All rights reserved.</p>
        </div>
      </div>

      <style jsx>{`
        .site-footer {
          background-color: #2d3748;
          color: #e2e8f0;
          padding: 60px 0 30px;
        }

        .container {
          max-width: 1200px;
          margin: 0 auto;
          padding: 0 20px;
        }

        .footer-content {
          display: flex;
          flex-wrap: wrap;
          justify-content: center;
          margin-bottom: 40px;
          gap: 20px;
        }

        .footer-bottom {
          text-align: center;
          padding-top: 20px;
          border-top: 1px solid #4a5568;
          font-size: 14px;
          color: #a0aec0;
        }

        @media (max-width: 768px) {
          .footer-content {
            flex-direction: column;
          }
        }
      `}</style>
    </footer>
  );
}

// Recursively render footer items
function FooterNavigationItem({ item }) {
  if (item.type === 'group') {
    return (
      item.children?.map((child) => (
        <Link
          href={resolveItemUrl(child)} key={child.id}
          target={child.target === '_blank' ? '_blank' : undefined}
          rel={child.target === '_blank' ? 'noopener noreferrer' : undefined}
        >
          {child.label}
        </Link>
      ))
    );
  }

  // If not a group, treat it as a single link section
  return (
    <Link
      href={resolveItemUrl(item)}
      target={item.target === '_blank' ? '_blank' : undefined}
      rel={item.target === '_blank' ? 'noopener noreferrer' : undefined}
    >
      {item.title}
    </Link>
  );
}

// Helper to resolve URL for page, post, or url types
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
