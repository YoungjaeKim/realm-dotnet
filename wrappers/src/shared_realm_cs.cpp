/* Copyright 2015 Realm Inc - All Rights Reserved
 * Proprietary and Confidential
 */
 
#include <realm.hpp>
#include <realm/lang_bind_helper.hpp>
#include "error_handling.hpp"
#include "realm_export_decls.hpp"
#include "marshalling.hpp"
#include "object-store/src/shared_realm.hpp"
#include "object-store/src/schema.hpp"

#include "debug.hpp"

#ifdef REALM_PLATFORM_ANDROID
#include "object-store/src/impl/android/cached_realm.hpp"
#endif


using namespace realm;
using namespace realm::binding;

extern "C" {

REALM_EXPORT SharedRealm* shared_realm_open(Schema* schema, uint16_t* path, size_t path_len, bool read_only, SharedGroup::DurabilityLevel durability,
                        uint8_t* encryption_key, uint64_t schemaVersion)
{
    return handle_errors([&]() {
        Utf16StringAccessor pathStr(path, path_len);

        Realm::Config config;
        config.path = pathStr.to_string();
        config.read_only = read_only;
        config.in_memory = durability != SharedGroup::durability_Full;

        // by definition the key is only allowwed to be 64 bytes long, enforced by C# code
        if (encryption_key == nullptr)
          config.encryption_key = std::vector<char>();
        else
          config.encryption_key = std::vector<char>(encryption_key, encryption_key+64);

        config.schema.reset(schema);
        config.schema_version = schemaVersion;
        return new SharedRealm{Realm::get_shared_realm(config)};
    });
}

REALM_EXPORT void shared_realm_destroy(SharedRealm* realm)
{
    handle_errors([&]() {
        delete realm;
    });
}

REALM_EXPORT size_t shared_realm_has_table(SharedRealm* realm, uint16_t* table_name, size_t table_name_len)
{
    return handle_errors([&]() {
        Group* g = (*realm)->read_group();
        Utf16StringAccessor str(table_name, table_name_len);

        return bool_to_size_t(g->has_table(str));
    });
}

REALM_EXPORT Table* shared_realm_get_table(SharedRealm* realm, uint16_t* table_name, size_t table_name_len)
{
    return handle_errors([&]() {
      Group* g = (*realm)->read_group();
      Utf16StringAccessor str(table_name, table_name_len);

      bool dummy; // get_or_add_table sets this to true if the table was added.
      return LangBindHelper::get_or_add_table(*g, str, &dummy);
    });
}

REALM_EXPORT uint64_t  shared_realm_get_schema_version(SharedRealm* realm)
{
    return handle_errors([&]() {
      return (*realm)->config().schema_version;
    });
}

REALM_EXPORT void shared_realm_begin_transaction(SharedRealm* realm)
{
    handle_errors([&]() {
        (*realm)->begin_transaction();
    });
}

REALM_EXPORT void shared_realm_commit_transaction(SharedRealm* realm)
{
    handle_errors([&]() {
        (*realm)->commit_transaction();
    });
}

REALM_EXPORT void shared_realm_cancel_transaction(SharedRealm* realm)
{
    handle_errors([&]() {
        (*realm)->cancel_transaction();
    });
}

REALM_EXPORT size_t shared_realm_is_in_transaction(SharedRealm* realm)
{
    return handle_errors([&]() {
        return bool_to_size_t((*realm)->is_in_transaction());
    });
}


REALM_EXPORT size_t shared_realm_is_same_instance(SharedRealm* lhs, SharedRealm* rhs)
{
    return handle_errors([&]() {
        return *lhs == *rhs;  // just compare raw pointers inside the smart pointers
    });
}

REALM_EXPORT size_t shared_realm_refresh(SharedRealm* realm)
{
    return handle_errors([&]() {
        return bool_to_size_t((*realm)->refresh());
    });
}

#ifdef REALM_PLATFORM_ANDROID

REALM_EXPORT void bind_handler_functions(realm::_impl::create_handler_function create_function, realm::_impl::notify_handler_function notify_function) 
{
    handle_errors([&]() {
        realm::_impl::create_handler_for_current_thread = create_function;
        realm::_impl::notify_handler = notify_function;
    });
}

REALM_EXPORT void notify_realm(std::shared_ptr<Realm>* realm)
{
  handle_errors([&]() {
    debug_log("Notify init");
    
    //if (realm->expired()) {
    //  debug_log("pointer was expired. Skipping.");
    //  return;
    //}
    //auto lock = realm->lock();
    //debug_log("Post locking");
    
    //lock->notify();
    (*realm)->notify();
    debug_log("Post notification");
  });
}

#endif

} // extern C
