"use client";

import Link from 'next/link';

export default function PricingSection({ tagline, headline, pricing_cards = [] }) {
  if (!pricing_cards || pricing_cards.length === 0) {
    return null;
  }

  return (
    <section className="pricing-section">
      <div className="container">
        <div className="pricing-header">
          {tagline && <p className="tagline">{tagline}</p>}
          {headline && <h2>{headline}</h2>}
        </div>

        <div className="pricing-plans">
          {pricing_cards.map((plan, index) => (
            <div className={`pricing-plan ${plan.is_highlighted ? 'featured' : ''}`} key={index}>
              {plan.badge && <span className="badge">{plan.badge}</span>}
              <h3>{plan.title}</h3>
              <div className="price">{plan.price}</div>
              {plan.description && <p className="plan-description">{plan.description}</p>}

              {plan.features && (
                <ul className="features">
                  {plan.features.map((feature, idx) => (
                    <li key={idx}>{feature.trim()}</li>
                  ))}
                </ul>
              )}

              {plan.button && plan.button.label && (
                <Link
                  href={resolveButtonUrl(plan.button)}
                  className={`cta-button ${plan.is_highlighted ? 'featured-cta' : ''}`}
                >
                  {plan.button.label}
                </Link>
              )}
            </div>
          ))}
        </div>
      </div>

      <style jsx>{`
        .pricing-section {
          padding: 80px 0;
          background-color: #ffffff;
        }

        .container {
          max-width: 1200px;
          margin: 0 auto;
          padding: 0 20px;
        }

        .pricing-header {
          text-align: center;
          margin-bottom: 60px;
        }

        .tagline {
          font-size: 18px;
          color: #718096;
          margin-bottom: 10px;
        }

        h2 {
          font-size: 36px;
          font-weight: bold;
          color: #1a202c;
          margin-bottom: 20px;
        }

        .pricing-plans {
          display: flex;
          flex-wrap: wrap;
          justify-content: center;
          gap: 30px;
        }

        .pricing-plan {
          background-color: #f7fafc;
          border-radius: 8px;
          padding: 40px;
          width: 300px;
          box-shadow: 0 4px 6px rgba(0, 0, 0, 0.05);
          transition: transform 0.3s;
          position: relative;
        }

        .pricing-plan:hover {
          transform: translateY(-10px);
        }

        .pricing-plan.featured {
          background-color: #ebf8ff;
          box-shadow: 0 8px 30px rgba(0, 0, 0, 0.1);
          border: 2px solid #3182ce;
          z-index: 1;
        }

        .badge {
          position: absolute;
          top: 20px;
          right: 20px;
          background-color: #3182ce;
          color: #ffffff;
          font-size: 12px;
          font-weight: 600;
          padding: 4px 8px;
          border-radius: 4px;
          text-transform: uppercase;
        }

        h3 {
          font-size: 24px;
          font-weight: 600;
          color: #2d3748;
          margin-bottom: 15px;
        }

        .price {
          font-size: 48px;
          font-weight: 700;
          color: #1a202c;
          margin-bottom: 20px;
        }

        .plan-description {
          font-size: 16px;
          color: #4a5568;
          margin-bottom: 30px;
          line-height: 1.5;
        }

        .features {
          list-style-type: none;
          padding: 0;
          margin: 0 0 30px;
        }

        .features li {
          padding: 8px 0;
          font-size: 16px;
          color: #4a5568;
          display: flex;
          align-items: center;
        }

        .features li:before {
          content: "✓";
          color: #38a169;
          font-weight: bold;
          margin-right: 10px;
        }

        .cta-button {
          display: block;
          width: 100%;
          background-color: #e2e8f0;
          color: #2d3748;
          text-align: center;
          padding: 12px 0;
          border-radius: 6px;
          font-weight: 600;
          text-decoration: none;
          transition: background-color 0.3s;
        }

        .cta-button:hover {
          background-color: #cbd5e0;
        }

        .featured-cta {
          background-color: #3182ce;
          color: white;
        }

        .featured-cta:hover {
          background-color: #2b6cb0;
        }

        @media (max-width: 768px) {
          .pricing-plan {
            width: 100%;
            max-width: 400px;
          }
        }
      `}</style>
    </section>
  );
}

// Helper function to resolve button URLs
function resolveButtonUrl(button) {
  if (button.type === 'page' && button.page) return `/pages/${button.page}`;
  if (button.type === 'post' && button.post) return `/posts/${button.post}`;
  return button.url || '#';
}
